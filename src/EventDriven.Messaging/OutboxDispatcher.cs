using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EventDriven.Messaging;

/// <summary>
/// Polls the outbox and publishes undispatched rows in insertion order, with publisher confirms,
/// marking a row dispatched only after the broker confirms (ADR-0003). Purges old dispatched rows.
/// </summary>
public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopes,
    RabbitConnection rabbit,
    IOptions<MessagingOptions> options,
    ILogger<OutboxDispatcher<TContext>> log) : BackgroundService
    where TContext : DbContext, IMessagingDbContext
{
    private readonly MessagingOptions _o = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        IChannel? channel = null;
        var lastPurge = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                channel ??= await rabbit.CreatePublishChannelAsync(ct);
                var published = await DispatchBatchAsync(channel, ct);

                if (DateTime.UtcNow - lastPurge > TimeSpan.FromMinutes(1))
                {
                    await PurgeAsync(ct);
                    lastPurge = DateTime.UtcNow;
                }

                if (published == 0) await Task.Delay(_o.OutboxPollMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox dispatch loop failed; backing off");
                if (channel is not null) { await channel.DisposeAsync(); channel = null; }
                await Task.Delay(2000, ct);
            }
        }

        if (channel is not null) await channel.DisposeAsync();
    }

    private async Task<int> DispatchBatchAsync(IChannel channel, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.DispatchedAt == null)
            .OrderBy(m => m.Seq)
            .Take(100)
            .ToListAsync(ct);

        foreach (var m in batch)
        {
            var parent = m.TraceParent is not null && ActivityContext.TryParse(m.TraceParent, null, out var pc)
                ? pc : default;
            using var act = Telemetry.Source.StartActivity($"publish {m.RoutingKey}", ActivityKind.Producer, parent);
            act?.SetTag("messaging.system", "rabbitmq");
            act?.SetTag("messaging.destination.name", m.Exchange);
            act?.SetTag("messaging.message.id", m.Id.ToString());

            // The consumer becomes a child of this publish span; fall back to the stored context.
            var traceParent = act?.Id ?? m.TraceParent;

            var props = new BasicProperties
            {
                Persistent = true,
                MessageId = m.Id.ToString(),
                CorrelationId = m.CorrelationId.ToString(),
                Type = m.RoutingKey,
                ContentType = "application/json",
                Headers = new Dictionary<string, object?>
                {
                    ["x-causation-id"] = m.CausationId?.ToString(),
                    ["x-occurred-at"] = m.OccurredAt.ToString("O"),
                    ["traceparent"] = traceParent
                }
            };

            // With publisher-confirmation tracking, this returns once the broker confirms (ADR-0003).
            await channel.BasicPublishAsync(
                m.Exchange, m.RoutingKey, mandatory: false, basicProperties: props,
                body: Encoding.UTF8.GetBytes(m.Body), cancellationToken: ct);

            m.DispatchedAt = DateTime.UtcNow;
        }

        if (batch.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Dispatched {Count} outbox message(s)", batch.Count);
        }
        return batch.Count;
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var cutoff = DateTime.UtcNow - _o.OutboxRetention;
        await db.OutboxMessages
            .Where(m => m.DispatchedAt != null && m.DispatchedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
