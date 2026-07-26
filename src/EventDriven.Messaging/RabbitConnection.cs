using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EventDriven.Messaging;

/// <summary>A lazily-opened, shared RabbitMQ connection. Channels are created per dispatcher/consumer.</summary>
public sealed class RabbitConnection(IOptions<MessagingOptions> options) : IAsyncDisposable
{
    private readonly MessagingOptions _o = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct = default)
    {
        if (_connection is { IsOpen: true }) return _connection;
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            var factory = new ConnectionFactory
            {
                HostName = _o.Host, Port = _o.Port, UserName = _o.User, Password = _o.Password
            };
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    /// <summary>A channel with publisher confirms enabled — the dispatcher needs the broker's ack (ADR-0003).</summary>
    public async Task<IChannel> CreatePublishChannelAsync(CancellationToken ct = default)
    {
        var conn = await GetAsync(ct);
        return await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken ct = default)
    {
        var conn = await GetAsync(ct);
        return await conn.CreateChannelAsync(cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
