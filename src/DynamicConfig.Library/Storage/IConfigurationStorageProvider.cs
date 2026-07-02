using DynamicConfig.Library.Models;

namespace DynamicConfig.Library.Storage;

/// <summary>
/// The storage seam of the library (see ADR 0003). The configuration reader depends on
/// this abstraction only — never on MongoDB — so the core is unit-testable with mocks
/// and the storage technology is swappable without touching reader code.
/// </summary>
/// <remarks>
/// Comparable to injecting a repository via an interface token in NestJS: the consumer
/// asks for the contract, composition decides the concrete provider.
/// </remarks>
public interface IConfigurationStorageProvider
{
    /// <summary>
    /// Returns every record that belongs to <paramref name="applicationName"/> and is
    /// currently active. Implementations MUST apply both conditions at the storage
    /// query level: records of other applications, or inactive ones, must never be
    /// materialized into the calling process.
    /// </summary>
    /// <param name="applicationName">The consuming service's name; must not be blank.</param>
    /// <param name="cancellationToken">Cancels the storage round-trip.</param>
    Task<IReadOnlyList<ConfigurationRecord>> GetActiveRecordsAsync(
        string applicationName,
        CancellationToken cancellationToken = default);
}
