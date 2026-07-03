namespace DynamicConfig.Library.Messaging;

/// <summary>
/// Broker-topology constants (ADR 0005) — one place, no magic strings. Public
/// for the same reason as <c>MongoConfigurationStorageDefaults</c>: the WebUI
/// publisher and the library consumer must agree on the exchange name, and a
/// shared constant makes a mismatch impossible at compile time (moved here from
/// the WebUI in 5.2, as flagged in the 5.1 close-out).
/// </summary>
public static class RabbitMqBrokerDefaults
{
    /// <summary>Fanout exchange carrying the thin config-changed signals.</summary>
    public const string ConfigChangedExchangeName = "dynamicconfig.config-changed";

    /// <summary>
    /// Environment variable holding the consumer's broker URI
    /// (e.g. <c>amqp://guest:guest@localhost:5672</c>). Present and non-blank →
    /// the reader starts the instant-refresh consumer next to polling; absent or
    /// blank → polling-only mode, zero broker code on the path. Opt-in by design:
    /// the case-frozen constructor has no broker parameter.
    /// </summary>
    public const string BrokerUriEnvironmentVariableName = "DYNAMIC_CONFIG_RABBITMQ_URI";
}
