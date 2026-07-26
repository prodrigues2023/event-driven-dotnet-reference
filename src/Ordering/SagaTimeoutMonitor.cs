using EventDriven.Contracts;
using EventDriven.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ordering;

/// <summary>
/// Closes the open edge in ADR-0007: a saga that waits for an event that never arrives. It periodically
/// sweeps for sagas stuck in a waiting state past a deadline and fires the timeout — cancel if no
/// payment was taken, refund (compensate) if one was. This is what turns "waits forever" into a
/// bounded, self-healing process.
/// </summary>
public sealed class SagaTimeoutMonitor(
    IServiceScopeFactory scopes,
    ILogger<SagaTimeoutMonitor> log,
    TimeSpan deadline) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Saga timeout monitor running; deadline {Deadline}s", deadline.TotalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try { await SweepAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Saga timeout sweep failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        List<Guid> stuck;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var cutoff = DateTime.UtcNow - deadline;
            stuck = await db.Sagas.AsNoTracking()
                .Where(s => (s.State == "AwaitingPayment" || s.State == "AwaitingShipment") && s.UpdatedAt < cutoff)
                .Select(s => s.OrderId).Take(50).ToListAsync(ct);
        }
        foreach (var id in stuck) await TimeoutOneAsync(id, ct);
    }

    private async Task TimeoutOneAsync(Guid orderId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var saga = await db.Sagas.FindAsync([orderId], ct);
        if (saga is null || (saga.State != "AwaitingPayment" && saga.State != "AwaitingShipment"))
        {
            await tx.RollbackAsync(ct); // already moved on between the sweep and now
            return;
        }
        var order = await db.Orders.FindAsync([orderId], ct);
        var outbox = new OutboxWriter(db, Exchanges.Ordering);

        if (saga.State == "AwaitingPayment")
        {
            // No money moved; just cancel.
            saga.State = "Cancelled";
            if (order is not null) order.Status = "PaymentFailed";
            log.LogWarning("Saga {Order} timed out awaiting payment — cancelled", orderId);
        }
        else // AwaitingShipment: payment succeeded but no shipment outcome — compensate.
        {
            saga.State = "Compensating";
            if (order is not null) order.Status = "Compensating";
            if (saga.PaymentId is { } paymentId && order is not null)
                outbox.SendCommand(orderId.ToString(), Commands.PaymentsQueue, Commands.RefundPayment,
                    new RefundPayment(orderId, paymentId, order.Amount, "saga timeout: no shipment outcome"));
            log.LogWarning("Saga {Order} timed out awaiting shipment — refunding", orderId);
        }

        saga.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
