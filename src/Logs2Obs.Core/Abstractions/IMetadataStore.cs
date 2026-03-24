namespace Logs2Obs.Core.Abstractions;

/// <summary>Cloud-agnostic metadata store abstraction for configuration and state.</summary>
public interface IMetadataStore
{
    /// <summary>Retrieves an entity by table and key, returning null if not found.</summary>
    Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default);

    /// <summary>Persists an entity to the specified table.</summary>
    Task PutAsync<T>(string table, T entity, CancellationToken ct = default);

    /// <summary>Deletes an entity from the specified table by key.</summary>
    Task DeleteAsync(string table, string key, CancellationToken ct = default);

    /// <summary>Queries entities from a table matching the given in-memory filter.</summary>
    IAsyncEnumerable<T> QueryAsync<T>(string table, Func<T, bool> filter, CancellationToken ct = default);
}
