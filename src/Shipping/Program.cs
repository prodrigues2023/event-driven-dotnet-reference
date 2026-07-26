using EventDriven.Contracts;
using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shipping;

var builder = Host.CreateApplicationBuilder(args);

var dbConn = Environment.GetEnvironmentVariable("DB_CONN")
    ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=shipping";

builder.Services.AddDbContext<ShippingDbContext>(o => o.UseNpgsql(dbConn));
builder.Services.AddEventDrivenMessaging(o =>
{
    o.Host = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
    o.EventExchange = Exchanges.Shipping;
});
builder.Services.AddEventDrivenTelemetry("shipping");
builder.Services.AddOutboxDispatcher<ShippingDbContext>();

builder.Services.AddEventConsumer<ShippingDbContext>(c =>
{
    c.QueueName = "shipping.payments.events";
    c.Bind(Exchanges.Payments, RoutingKeys.PaymentAuthorized);

    c.On(RoutingKeys.PaymentAuthorized, async (ctx, ct) =>
    {
        var e = ctx.Envelope.Payload<PaymentAuthorized>();

        // High-value orders have no automatic carrier here — shipping fails as a business outcome,
        // which triggers the saga to compensate the (already successful) payment.
        if (e.Amount > 2000m)
        {
            ctx.Outbox.Publish(e.OrderId.ToString(), RoutingKeys.ShipmentFailed,
                new ShipmentFailed(e.OrderId, "no carrier for high-value shipment; manual handling required"), ctx.Envelope);
            return;
        }

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = e.OrderId,
            TrackingNumber = "TRK-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            ShippedAt = DateTime.UtcNow
        };
        ctx.Db.Shipments.Add(shipment);
        ctx.Outbox.Publish(e.OrderId.ToString(), RoutingKeys.OrderShipped,
            new OrderShipped(e.OrderId, shipment.Id, shipment.TrackingNumber), ctx.Envelope);
        await Task.CompletedTask;
    });
});

var app = builder.Build();
await Startup.MigrateAsync<ShippingDbContext>(app.Services);
await app.RunAsync();
