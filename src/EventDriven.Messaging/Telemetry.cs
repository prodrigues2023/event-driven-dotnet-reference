using System.Diagnostics;

namespace EventDriven.Messaging;

/// <summary>The activity source for messaging spans — publish on the producer, consume on the consumer.
/// The W3C trace context rides in the message's <c>traceparent</c> header, so a single order's journey
/// across Ordering, Payments, and Shipping is one distributed trace.</summary>
public static class Telemetry
{
    public const string SourceName = "EventDriven.Messaging";
    public static readonly ActivitySource Source = new(SourceName);
}
