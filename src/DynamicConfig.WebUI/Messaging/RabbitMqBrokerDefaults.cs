namespace DynamicConfig.WebUI.Messaging;

/// <summary>
/// Broker-topology constants (ADR 0005) — one place, no magic strings.
/// PLACEMENT NOTE: the 5.2 consumer (library side) must reference the SAME
/// exchange name. Whether this constant then moves into the library (the
/// MongoConfigurationStorageDefaults shared-kernel pattern) or is duplicated
/// and verified is a public-surface decision reserved for the user at 5.2
/// kickoff — 5.1 deliberately keeps it WebUI-local because 5.1 changes no
/// library code.
/// </summary>
public static class RabbitMqBrokerDefaults
{
    /// <summary>Fanout exchange carrying the thin config-changed signals.</summary>
    public const string ConfigChangedExchangeName = "dynamicconfig.config-changed";
}
