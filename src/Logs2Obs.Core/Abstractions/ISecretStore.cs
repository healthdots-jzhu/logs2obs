namespace Logs2Obs.Core.Abstractions;

/// <summary>Cloud-agnostic secret store abstraction for reading and writing secrets.</summary>
public interface ISecretStore
{
    /// <summary>Retrieves the value of the named secret, or null if not found.</summary>
    Task<string?> GetSecretAsync(string name, CancellationToken ct = default);

    /// <summary>Sets or updates the value of the named secret.</summary>
    Task SetSecretAsync(string name, string value, CancellationToken ct = default);
}
