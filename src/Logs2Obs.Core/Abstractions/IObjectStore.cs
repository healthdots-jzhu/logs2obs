namespace Logs2Obs.Core.Abstractions;

/// <summary>Cloud-agnostic object/blob storage abstraction.</summary>
public interface IObjectStore
{
    /// <summary>Writes content to the specified key.</summary>
    Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Reads the content at the specified key, or null if not found.</summary>
    Task<Stream?> ReadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the object at the specified key.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Returns true if an object exists at the specified key.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Lists all keys matching the given prefix.</summary>
    IAsyncEnumerable<string> ListAsync(string prefix, CancellationToken ct = default);
}
