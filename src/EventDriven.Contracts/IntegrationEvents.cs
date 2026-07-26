namespace EventDriven.Contracts;

// The integration events that cross service boundaries. In Milestone 2 these gain JSON Schemas
// and a versioning policy; here they are the shared, minimal contract the reference builds on.

public record OrderPlaced(Guid OrderId, string Customer, decimal Amount, DateTime PlacedAt);

public record PaymentAuthorized(Guid OrderId, Guid PaymentId, decimal Amount);

public record PaymentDeclined(Guid OrderId, string Reason);

public record OrderShipped(Guid OrderId, Guid ShipmentId, string TrackingNumber);

public record ShipmentFailed(Guid OrderId, string Reason);

public record PaymentRefunded(Guid OrderId, Guid PaymentId, decimal Amount);

// Commands (ADR-0002): an instruction to exactly one handler, not a fact broadcast to subscribers.
// The order-fulfilment saga (ADR-0007) issues this to compensate a shipment that failed after payment.
public record RefundPayment(Guid OrderId, Guid PaymentId, decimal Amount, string Reason);

/// <summary>Routing keys are a public contract (ADR-0002): the event type in dotted lower case.</summary>
public static class RoutingKeys
{
    public const string OrderPlaced = "order.placed";
    public const string PaymentAuthorized = "payment.authorized";
    public const string PaymentDeclined = "payment.declined";
    public const string OrderShipped = "order.shipped";
    public const string ShipmentFailed = "shipment.failed";
    public const string PaymentRefunded = "payment.refunded";
}

/// <summary>One topic exchange per bounded context (ADR-0002), named {context}.events.</summary>
public static class Exchanges
{
    public const string Ordering = "ordering.events";
    public const string Payments = "payments.events";
    public const string Shipping = "shipping.events";
}

/// <summary>Commands are sent to a single queue owned by the handling service (ADR-0002).</summary>
public static class Commands
{
    public const string PaymentsQueue = "payments.commands";
    public const string RefundPayment = "payment.refund";
}
