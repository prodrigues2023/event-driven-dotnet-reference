using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventDriven.Messaging;

/// <summary>
/// Consumes one durable queue, deduplicating via the inbox (ADR-0004) and applying the three-layer
/// retry / dead-letter strategy (ADR-0005): in-process retries for transient faults, then a delayed
/// retry ladder through the broker, then a dead-letter queue — never <c>requeue: true</c>.
/// </summary>
public sealed class ConsumerHost<TContext>(
    IServiceScopeFactory scopes,
    RabbitConnection rabbit,
    EventConsumerOptions<TContext> config,
    IOptions<MessagingOptions> options,
    ILogger<ConsumerHost<TContext>> log) : BackgroundService
    where TContext : DbContext, IMessagingDbContext
{
    private readonly MessagingOptions _o = options.Value;
    private IChannel _consume = null!;
    private IChannel _publish = null!;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _consume = await rabbit.CreateChannelAsync(ct);
                _publish = await rabbit.CreatePublishChannelAsync(ct);
                await DeclareTopologyAsync(ct);
                await _consume.BasicQosAsync(0, _o.Prefetch, global: false, ct);

                var consumer = new AsyncEventingBasicConsumer(_consume);
                consumer.ReceivedAsync += OnReceivedAsync;
                await _consume.BasicConsumeAsync(config.QueueName, autoAck: false, consumer, ct);
                log.LogInformation("Consuming {Queue}", config.QueueName);

                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Consumer {Queue} setup failed; retrying in 2s", config.QueueName);
                await DisposeChannelsAsync();
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }
        await DisposeChannelsAsync();
    }

    private async Task DisposeChannelsAsync()
    {
        if (_consume is not null) { try { await _consume.DisposeAsync(); } catch { } _consume = null!; }
        if (_publish is not null) { try { await _publish.DisposeAsync(); } catch { } _publish = null!; }
    }

    private async Task DeclareTopologyAsync(CancellationToken ct)
    {
        foreach (var (exchange, _) in config.Bindings.DistinctBy(b => b.Exchange))
            await _consume.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

        await _consume.QueueDeclareAsync(config.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        foreach (var (exchange, pattern) in config.Bindings)
            await _consume.QueueBindAsync(config.QueueName, exchange, pattern, cancellationToken: ct);

        await _consume.QueueDeclareAsync($"{config.QueueName}.dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        // A delay queue per retry level: TTL, then dead-letter back to the work queue (ADR-0005).
        for (var i = 0; i < _o.RetryDelaysSeconds.Length; i++)
        {
            await _consume.QueueDeclareAsync($"{config.QueueName}.retry.{i}", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = _o.RetryDelaysSeconds[i] * 1000,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = config.QueueName
                }, cancellationToken: ct);
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var ct = CancellationToken.None;
        MessageEnvelope env;
        try { env = Parse(ea); }
        catch (Exception ex)
        {
            log.LogError(ex, "Unparseable message, dead-lettering");
            await DeadLetterAsync(ea, "unparseable", ex);
            await _consume.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            return;
        }

        var tp = TryGetString(ea.BasicProperties.Headers, "traceparent");
        var parent = tp is not null && ActivityContext.TryParse(tp, null, out var pc) ? pc : default;
        using var act = Telemetry.Source.StartActivity($"consume {env.Type}", ActivityKind.Consumer, parent);
        act?.SetTag("messaging.system", "rabbitmq");
        act?.SetTag("messaging.destination.name", config.QueueName);
        act?.SetTag("messaging.message.id", env.MessageId.ToString());

        try
        {
            await HandleWithInProcessRetryAsync(env, ct);
            await _consume.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (PoisonMessageException ex)
        {
            log.LogWarning("Poison message {Id} ({Type}): {Reason}", env.MessageId, env.Type, ex.Message);
            act?.SetStatus(ActivityStatusCode.Error, "poison");
            await DeadLetterAsync(ea, "permanent", ex);
            await _consume.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex)
        {
            act?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var attempt = ReadAttempt(ea);
            if (attempt < _o.RetryDelaysSeconds.Length)
            {
                log.LogWarning(ex, "Transient failure on {Id}; delayed retry {Attempt}", env.MessageId, attempt);
                await ScheduleRetryAsync(ea, attempt);
            }
            else
            {
                log.LogError(ex, "Retries exhausted on {Id}; dead-lettering", env.MessageId);
                await DeadLetterAsync(ea, "retries-exhausted", ex);
            }
            await _consume.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
    }

    private async Task HandleWithInProcessRetryAsync(MessageEnvelope env, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await HandleOnceAsync(env, ct); return; }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                log.LogInformation("Duplicate {Id} ({Type}) already processed — skipping", env.MessageId, env.Type);
                return; // exactly-once effect (ADR-0004)
            }
            catch (PoisonMessageException) { throw; }
            catch (Exception) when (attempt < _o.InProcessRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1) + Random.Shared.Next(50)), ct);
            }
        }
    }

    private async Task HandleOnceAsync(MessageEnvelope env, CancellationToken ct)
    {
        if (!config.Handlers.TryGetValue(env.Type, out var handler))
        {
            log.LogWarning("No handler for {Type} on {Queue} — acknowledging", env.Type, config.QueueName);
            return;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.InboxMessages.Add(new InboxMessage { MessageId = env.MessageId, ProcessedAt = DateTime.UtcNow });
        var outbox = new OutboxWriter(db, _o.EventExchange);
        await handler(new InboundContext<TContext> { Envelope = env, Db = db, Outbox = outbox }, ct);

        await db.SaveChangesAsync(ct); // effect + inbox + new outbox rows commit atomically
        await tx.CommitAsync(ct);
    }

    private async Task ScheduleRetryAsync(BasicDeliverEventArgs ea, int attempt)
    {
        var props = CloneProps(ea);
        props.Headers ??= new Dictionary<string, object?>();
        props.Headers["x-delivery-attempt"] = attempt + 1;
        await _publish.BasicPublishAsync("", $"{config.QueueName}.retry.{attempt}", mandatory: false,
            basicProperties: props, body: ea.Body, cancellationToken: CancellationToken.None);
    }

    private async Task DeadLetterAsync(BasicDeliverEventArgs ea, string reason, Exception ex)
    {
        var props = CloneProps(ea);
        props.Headers ??= new Dictionary<string, object?>();
        props.Headers["x-death-reason"] = reason;
        props.Headers["x-original-queue"] = config.QueueName;
        props.Headers["x-attempts"] = ReadAttempt(ea);
        props.Headers["x-exception"] = $"{ex.GetType().Name}: {ex.Message}";
        props.Headers["x-dead-lettered-at"] = DateTime.UtcNow.ToString("O");
        await _publish.BasicPublishAsync("", $"{config.QueueName}.dlq", mandatory: false,
            basicProperties: props, body: ea.Body, cancellationToken: CancellationToken.None);
    }

    private static BasicProperties CloneProps(BasicDeliverEventArgs ea)
    {
        var p = ea.BasicProperties;
        return new BasicProperties
        {
            Persistent = true,
            MessageId = p.MessageId,
            CorrelationId = p.CorrelationId,
            Type = p.Type,
            ContentType = p.ContentType ?? "application/json",
            Headers = p.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(p.Headers)
        };
    }

    private static MessageEnvelope Parse(BasicDeliverEventArgs ea)
    {
        var p = ea.BasicProperties;
        var id = Guid.Parse(p.MessageId ?? throw new PoisonMessageException("missing MessageId"));
        var type = p.Type ?? ea.RoutingKey;
        var correlation = Guid.TryParse(p.CorrelationId, out var c) ? c : Guid.NewGuid();
        Guid? causation = TryGetString(p.Headers, "x-causation-id") is { } cs && Guid.TryParse(cs, out var cg) ? cg : null;
        var occurred = TryGetString(p.Headers, "x-occurred-at") is { } os && DateTime.TryParse(os, out var d) ? d : DateTime.UtcNow;
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        return new MessageEnvelope(id, type, correlation, causation, occurred, body);
    }

    private static int ReadAttempt(BasicDeliverEventArgs ea) =>
        ea.BasicProperties.Headers?.TryGetValue("x-delivery-attempt", out var v) == true && v is not null
            ? Convert.ToInt32(v) : 0;

    private static string? TryGetString(IDictionary<string, object?>? headers, string key) =>
        headers?.TryGetValue(key, out var v) == true && v is byte[] bytes ? Encoding.UTF8.GetString(bytes)
        : headers?.TryGetValue(key, out var s) == true ? s?.ToString() : null;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
