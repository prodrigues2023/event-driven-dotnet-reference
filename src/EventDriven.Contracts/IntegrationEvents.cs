namespace EventDriven.Contracts;

// The integration events that cross service boundaries. In Milestone 2 these gain JSON Schemas
// and a versioning policy; here they are the shared, minimal contract the reference builds on.

public record OrderPlaced(Guid OrderId, string Customer, decimal Amount, DateTime PlacedAt);

public record PaymentAuthorized(Guid OrderId, Guid PaymentId, decimal Amount);

public record PaymentDeclined(Guid OrderId, string Reason);

public record OrderShipped(Guid OrderId, Guid ShipmentId, string TrackingNumber);

/// <summary>Routing keys are a public contract (ADR-0002): the event type in dotted lower case.</summary>
public static class RoutingKeys
{
    public const string OrderPlaced = "order.placed";
    public const string PaymentAuthorized = "payment.authorized";
    public const string PaymentDeclined = "payment.declined";
    public const string OrderShipped = "order.shipped";
}

/// <summary>One topic exchange per bounded context (ADR-0002), named {context}.events.</summary>
public static class Exchanges
{
    public const string Ordering = "ordering.events";
    public const string Payments = "payments.events";
    public const string Shipping = "shipping.events";
}
