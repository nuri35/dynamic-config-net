using System.Diagnostics;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DynamicConfig.Library.Messaging;

/// <summary>
/// RabbitMQ implementation of the consumer seam (ADR 0005): an exclusive
/// auto-delete queue bound to the shared fanout exchange — the queue dies with
/// this instance, no orphans. Messages are auto-acked: a failed refresh is NOT
/// nack/requeued, because the next poll self-heals (same no-retry philosophy as
/// the publisher's fire-and-forget).
/// </summary>
internal sealed class RabbitMqConfigurationChangeSource : IConfigurationChangeSource
{
    private readonly Uri _brokerUri;
    private IConnection? _connection;
    private IChannel? _channel;

    internal RabbitMqConfigurationChangeSource(string brokerUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerUri);
        _brokerUri = new Uri(brokerUri);
    }

    /// <inheritdoc />
    public async Task StartAsync(
        Func<ConfigurationChangedEvent, Task> onConfigurationChanged,
        CancellationToken cancellationToken)
    {
        var connectionFactory = new ConnectionFactory
        {
            Uri = _brokerUri,
            // Built-in recovery re-establishes connection, channel, queue and
            // binding (exclusive queues are re-declared under a new server-named
            // identity) after a drop. While down, polling carries — deliberately
            // no custom retry machinery (5.1's no-outbox decision, consumer side).
            AutomaticRecoveryEnabled = true,
        };

        _connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // Same declare as the publisher (idempotent server-side); the shared
        // constant guarantees both sides name the same exchange.
        await _channel.ExchangeDeclareAsync(
                RabbitMqBrokerDefaults.ConfigChangedExchangeName,
                ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Server-named exclusive auto-delete queue: per-instance, vanishes with it.
        var queue = await _channel.QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _channel.QueueBindAsync(
                queue.QueueName,
                RabbitMqBrokerDefaults.ConfigChangedExchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            // Parse-or-drop: a malformed body is logged and skipped — one bad
            // message must never kill the consumer.
            if (!ConfigurationChangedEvent.TryParse(delivery.Body.Span, out var changedEvent))
            {
                Trace.TraceWarning("DynamicConfig: dropped malformed config-changed message.");
                return;
            }

            await onConfigurationChanged(changedEvent).ConfigureAwait(false);
        };

        // autoAck: no nack/requeue choreography by decision — polling self-heals.
        await _channel.BasicConsumeAsync(
                queue.QueueName,
                autoAck: true,
                consumer,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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
    }
}
