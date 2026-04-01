namespace Logs2Obs.Api.Auth;

/// <summary>
/// Configuration wrapper for all registered identity providers.
/// Bound from the <c>Auth</c> configuration section.
/// When <see cref="IdentityProviders"/> is empty, the system falls back to the
/// legacy single-scheme JWT configuration from the <c>Jwt</c> section.
/// </summary>
public sealed class MultiIdpOptions
{
    /// <summary>Gets the registered identity providers.</summary>
    public IdentityProviderOptions[] IdentityProviders { get; init; } = [];
}
