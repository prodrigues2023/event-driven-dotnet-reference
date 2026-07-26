using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ordering;

public class Order
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Placed"; // Placed | Paid | Shipped | PaymentFailed | Compensating | Cancelled
    public DateTime PlacedAt { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ShipmentId { get; set; }
    public string? TrackingNumber { get; set; }
}

/// <summary>
/// The order-fulfilment saga state (ADR-0007): an orchestrated state machine that lives in the
/// initiating context and drives compensation. It remembers the payment so that, if shipping fails
/// after payment, it can issue a refund command — the coordinator choreography could not provide.
/// </summary>
public class OrderSaga
{
    public Guid OrderId { get; set; }
    // AwaitingPayment | AwaitingShipment | Completed | Cancelled | Compensating | Compensated
    public string State { get; set; } = "AwaitingPayment";
    public Guid? PaymentId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderSaga> Sagas => Set<OrderSaga>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>().ToTable("orders");
        mb.Entity<OrderSaga>().ToTable("order_sagas").HasKey(x => x.OrderId);
        mb.ConfigureMessaging();
    }
}
