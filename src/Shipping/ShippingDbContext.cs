using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Shipping;

public class Shipment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string TrackingNumber { get; set; } = "";
    public DateTime ShippedAt { get; set; }
}

public class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Shipment>().ToTable("shipments");
        mb.ConfigureMessaging();
    }
}
