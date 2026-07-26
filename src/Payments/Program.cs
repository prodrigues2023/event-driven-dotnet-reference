using EventDriven.Contracts;
using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Payments;

var builder = Host.CreateApplicationBuilder(args);

var dbConn = Environment.GetEnvironmentVariable("DB_CONN")
    ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=payments";

builder.Services.AddDbContext<PaymentsDbContext>(o => o.UseNpgsql(dbConn));
builder.Services.AddEventDrivenMessaging(o =>
{
    o.Host = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
    o.EventExchange = Exchanges.Payments;
});
builder.Services.AddEventDrivenTelemetry("payments");
builder.Services.AddOutboxDispatcher<PaymentsDbContext>();

builder.Services.AddEventConsumer<PaymentsDbContext>(c =>
{
    c.QueueName = "payments.ordering.events";
    c.Bind(Exchanges.Ordering, RoutingKeys.OrderPlaced);

    c.On(RoutingKeys.OrderPlaced, async (ctx, ct) =>
    {
        var e = ctx.Envelope.Payload<OrderPlaced>();

        // A malformed order is a poison message: fail fast to the DLQ, no retries (ADR-0005).
        if (e.Amount <= 0)
            throw new PoisonMessageException($"order {e.OrderId} has a non-positive amount ({e.Amount})");

        var payment = new Payment { Id = Guid.NewGuid(), OrderId = e.OrderId, Amount = e.Amount, ProcessedAt = DateTime.UtcNow };

        // A decline is a valid business outcome (an event), not a failure — over the authorization limit.
        if (e.Amount > 5000m)
        {
            payment.Status = "Declined";
            ctx.Db.Payments.Add(payment);
            ctx.Outbox.Publish(e.OrderId.ToString(), RoutingKeys.PaymentDeclined,
                new PaymentDeclined(e.OrderId, "amount exceeds the authorization limit"), ctx.Envelope);
        }
        else
        {
            payment.Status = "Authorized";
            ctx.Db.Payments.Add(payment);
            ctx.Outbox.Publish(e.OrderId.ToString(), RoutingKeys.PaymentAuthorized,
                new PaymentAuthorized(e.OrderId, payment.Id, e.Amount), ctx.Envelope);
        }
        await Task.CompletedTask;
    });
});

var app = builder.Build();
await Startup.MigrateAsync<PaymentsDbContext>(app.Services);
await app.RunAsync();
