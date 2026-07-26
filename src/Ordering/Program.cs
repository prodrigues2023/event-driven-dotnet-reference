using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EventDriven.Contracts;
using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;
using Ordering;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var dbConn = Environment.GetEnvironmentVariable("DB_CONN")
    ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=ordering";

builder.Services.AddDbContext<OrderingDbContext>(o => o.UseNpgsql(dbConn));
builder.Services.AddEventDrivenMessaging(o =>
{
    o.Host = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
    o.EventExchange = Exchanges.Ordering;
});
builder.Services.AddOutboxWriter<OrderingDbContext>();
builder.Services.AddOutboxDispatcher<OrderingDbContext>();

// Ordering also listens for what happens to its orders downstream, and updates their status.
builder.Services.AddEventConsumer<OrderingDbContext>(c =>
{
    c.QueueName = "ordering.inbox";
    c.Bind(Exchanges.Payments, "payment.*");
    c.Bind(Exchanges.Shipping, RoutingKeys.OrderShipped);

    c.On(RoutingKeys.PaymentAuthorized, async (ctx, ct) =>
    {
        var e = ctx.Envelope.Payload<PaymentAuthorized>();
        var order = await ctx.Db.Orders.FindAsync([e.OrderId], ct);
        if (order is { Status: not "Shipped" }) { order.Status = "Paid"; order.PaymentId = e.PaymentId; }
    });
    c.On(RoutingKeys.PaymentDeclined, async (ctx, ct) =>
    {
        var e = ctx.Envelope.Payload<PaymentDeclined>();
        var order = await ctx.Db.Orders.FindAsync([e.OrderId], ct);
        if (order is not null) order.Status = "PaymentFailed";
    });
    c.On(RoutingKeys.OrderShipped, async (ctx, ct) =>
    {
        var e = ctx.Envelope.Payload<OrderShipped>();
        var order = await ctx.Db.Orders.FindAsync([e.OrderId], ct);
        if (order is not null) { order.Status = "Shipped"; order.ShipmentId = e.ShipmentId; order.TrackingNumber = e.TrackingNumber; }
    });
});

var app = builder.Build();
await Startup.MigrateAsync<OrderingDbContext>(app.Services);

// Serve the live process console at / (wwwroot), same origin as the API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Broker queue depths (work + retry + DLQ), read from the RabbitMQ management API server-side
// so the browser console can show them without CORS.
app.MapGet("/system/queues", async (IHttpClientFactory httpFactory) =>
{
    var host = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";
    var user = Environment.GetEnvironmentVariable("RABBIT_USER") ?? "guest";
    var pass = Environment.GetEnvironmentVariable("RABBIT_PASS") ?? "guest";
    var http = httpFactory.CreateClient();
    http.Timeout = TimeSpan.FromSeconds(3);
    var req = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:15672/api/queues/%2F");
    req.Headers.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
    try
    {
        using var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return Results.Ok(Array.Empty<object>());
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var prefixes = new[] { "ordering.inbox", "payments.ordering.events", "shipping.payments.events" };
        var list = doc.RootElement.EnumerateArray()
            .Select(q => new
            {
                name = q.GetProperty("name").GetString() ?? "",
                messages = q.TryGetProperty("messages", out var m) && m.TryGetInt32(out var v) ? v : 0
            })
            .Where(q => prefixes.Any(p => q.name.StartsWith(p)))
            .OrderBy(q => q.name)
            .ToList();
        return Results.Ok(list);
    }
    catch { return Results.Ok(Array.Empty<object>()); }
});

app.MapPost("/orders", async (PlaceOrder req, OrderingDbContext db, OutboxWriter outbox) =>
{
    if (string.IsNullOrWhiteSpace(req.Customer)) return Results.BadRequest(new { error = "customer is required" });

    var order = new Order { Id = Guid.NewGuid(), Customer = req.Customer, Amount = req.Amount, Status = "Placed", PlacedAt = DateTime.UtcNow };
    db.Orders.Add(order);
    // The event is written to the outbox in the SAME transaction as the order (ADR-0003).
    outbox.Publish(order.Id.ToString(), RoutingKeys.OrderPlaced,
        new OrderPlaced(order.Id, order.Customer, order.Amount, order.PlacedAt));
    await db.SaveChangesAsync();

    return Results.Created($"/orders/{order.Id}", ToDto(order));
});

app.MapGet("/orders/{id:guid}", async (Guid id, OrderingDbContext db) =>
    await db.Orders.FindAsync(id) is { } o ? Results.Ok(ToDto(o)) : Results.NotFound());

app.MapGet("/orders", async (OrderingDbContext db) =>
    Results.Ok((await db.Orders.OrderByDescending(o => o.PlacedAt).Take(50).ToListAsync()).Select(ToDto)));

// Replay: re-dispatch the original OrderPlaced with its SAME MessageId (ADR-0003: clear the flag).
// Downstream consumers see a duplicate and skip it via the inbox (ADR-0004) — exactly-once effect.
app.MapPost("/orders/{id:guid}/replay", async (Guid id, OrderingDbContext db) =>
{
    var rows = await db.OutboxMessages
        .Where(m => m.AggregateId == id.ToString() && m.RoutingKey == RoutingKeys.OrderPlaced)
        .ToListAsync();
    if (rows.Count == 0) return Results.NotFound();
    foreach (var r in rows) r.DispatchedAt = null;
    await db.SaveChangesAsync();
    return Results.Ok(new { replayed = rows.Count, messageId = rows[0].Id });
});

app.Run();

static object ToDto(Order o) => new
{
    o.Id, o.Customer, o.Amount, o.Status, o.PlacedAt, o.PaymentId, o.ShipmentId, o.TrackingNumber
};

record PlaceOrder(string Customer, decimal Amount);
