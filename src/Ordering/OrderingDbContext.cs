using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ordering;

public class Order
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Placed"; // Placed | Paid | PaymentFailed | Shipped
    public DateTime PlacedAt { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ShipmentId { get; set; }
    public string? TrackingNumber { get; set; }
}

public class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>().ToTable("orders");
        mb.ConfigureMessaging();
    }
}
