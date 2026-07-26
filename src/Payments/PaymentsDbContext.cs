using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Payments;

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = ""; // Authorized | Declined
    public DateTime ProcessedAt { get; set; }
}

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Payment>().ToTable("payments");
        mb.ConfigureMessaging();
    }
}
