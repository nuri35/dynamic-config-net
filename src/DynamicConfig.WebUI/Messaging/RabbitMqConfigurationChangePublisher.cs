using DynamicConfig.Library.Messaging;
using RabbitMQ.Client;

namespace DynamicConfig.WebUI.Messaging;

/// <summary>
/// RabbitMQ implementation of the change-signal seam (ADR 0005): one fanout
/// exchange, thin JSON events. Connects lazily on the first publish — the WebUI
/// must boot and serve fully with the broker down; a failed publish surfaces to
/// the service, which logs and continues (polling is the guaranteed carrier).
/// </summary>
public sealed class RabbitMqConfigurationChangePublisher : IConfigurationChangePublisher, IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;

    // The channel is created once and reused; RabbitMQ.Client's automatic
    // recovery re-establishes a dropped connection under the same objects.
    // SemaphoreSlim (not lock) because the guarded section awaits.
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    /// <remarks>
    /// Construction never touches the network — same principle as the Mongo
    /// provider: "broker unreachable" is a runtime concern the write path
    /// survives, never a boot blocker.
    /// </remarks>
    public RabbitMqConfigurationChangePublisher(string amqpUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amqpUri);

        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(amqpUri),
            AutomaticRecoveryEnabled = true,
        };
    }

    /// <inheritdoc />
    public async Task PublishChangedAsync(string applicationName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        // The wire contract lives in the library (5.2 shared kernel): publisher
        // and consumer serialize/parse the SAME type — drift is impossible.
        var body = new ConfigurationChangedEvent(applicationName, DateTime.UtcNow).ToUtf8Json();

        // Fanout ignores the routing key; empty by convention.
        await channel
            .BasicPublishAsync(
                RabbitMqBrokerDefaults.ConfigChangedExchangeName,
                routingKey: string.Empty,
                body: body,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates connection + channel on first use and declares the fanout exchange
    /// (idempotent server-side: same name + same properties is a no-op). A closed
    /// channel/connection is disposed and rebuilt on the next publish attempt, so
    /// a broker restart heals without recycling the WebUI.
    /// </summary>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        var openChannel = _channel;
        if (openChannel is { IsOpen: true })
        {
            return openChannel;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            if (_channel is not null)
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
            }

            if (_connection is not { IsOpen: true })
            {
                if (_connection is not null)
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }

                _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel
                .ExchangeDeclareAsync(
                    RabbitMqBrokerDefaults.ConfigChangedExchangeName,
                    ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _channel = channel;
            return channel;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _connectionLock.Dispose();
    }
}
