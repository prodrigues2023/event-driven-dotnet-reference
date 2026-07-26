namespace EventDriven.Messaging;

public sealed class MessagingOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>This service's own events exchange (ADR-0002), e.g. "ordering.events".</summary>
    public string EventExchange { get; set; } = "";

    /// <summary>Outbox dispatcher poll interval. The central latency trade-off of ADR-0003.</summary>
    public int OutboxPollMs { get; set; } = 200;

    /// <summary>Dispatched rows older than this are purged. This window is the replay window (ADR-0003).</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Layer-1 in-process retries for transient faults (ADR-0005).</summary>
    public int InProcessRetries { get; set; } = 3;

    /// <summary>Layer-2 delayed-retry ladder, in seconds (ADR-0005). Kept short here so the demo
    /// does not wait 43 minutes; the ADR's production ladder is 5s / 30s / 2m / 10m / 30m.</summary>
    public int[] RetryDelaysSeconds { get; set; } = { 3, 8, 20 };

    /// <summary>Consumer prefetch (unacked messages in flight per consumer).</summary>
    public ushort Prefetch { get; set; } = 10;
}
