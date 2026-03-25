using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;

namespace Logs2Obs.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/keys")
            .RequireAuthorization();

        group.MapGet("", ListApiKeys)
            .WithName("ListApiKeys")
            .WithOpenApi();

        group.MapPost("", CreateApiKey)
            .WithName("CreateApiKey")
            .WithOpenApi();

        group.MapDelete("/{keyId}", DeleteApiKey)
            .WithName("DeleteApiKey")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> ListApiKeys(
        HttpContext context,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var keys = new List<object>();
        
        await foreach (var key in metadataStore.QueryAsync<Dictionary<string, string>>("api_keys", _ => true, cancellationToken))
        {
            keys.Add(key);
        }

        return Results.Ok(keys);
    }

    private static async Task<IResult> CreateApiKey(
        HttpContext context,
        CreateApiKeyRequest request,
        IMetadataStore metadataStore,
        ISecretStore secretStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var keyId = Guid.NewGuid().ToString();
        var apiKey = GenerateApiKey();

        await secretStore.SetSecretAsync($"apikey:{tenantId}:{keyId}", apiKey, cancellationToken);

        var keyMetadata = new Dictionary<string, string>
        {
            ["keyId"] = keyId,
            ["tenantId"] = tenantId,
            ["name"] = request.Name,
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
            ["expiresAt"] = request.ExpiresAt ?? "",
            ["permissions"] = request.Permissions != null ? System.Text.Json.JsonSerializer.Serialize(request.Permissions) : "",
            ["active"] = "true"
        };

        await metadataStore.PutAsync("api_keys", keyMetadata, cancellationToken);

        return Results.Ok(new
        {
            keyId,
            apiKey,
            name = request.Name,
            warning = "Store this API key securely. It will not be shown again."
        });
    }

    private static async Task<IResult> DeleteApiKey(
        HttpContext context,
        string keyId,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var metadata = await metadataStore.GetAsync<Dictionary<string, string>>("api_keys", keyId, cancellationToken);

        if (metadata == null)
        {
            return Results.NotFound();
        }

        metadata["active"] = "false";
        metadata["deletedAt"] = DateTime.UtcNow.ToString("O");

        await metadataStore.PutAsync("api_keys", metadata, cancellationToken);

        return Results.Ok(new { keyId, deleted = true });
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
