using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Logs2Obs.Api.Auth;

namespace Logs2Obs.Api.Tests.Auth;

/// <summary>
/// Tests for ApiKeyAuthHandler (to be created by Maeve in Phase 4).
/// Tests are written against expected contract: validates API key from X-API-Key header,
/// caches results, queries IMetadataStore on cache miss, sets TenantId claim.
/// </summary>
public class ApiKeyAuthHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidApiKeyAndCacheHit_ReturnsSuccess()
    {
        // Arrange
        var apiKey = "valid-key-123";
        var cacheKey = $"apikey:{apiKey}";
        var cachedValue = ("t-abc", "key-123");
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        
        object? cacheValue = cachedValue;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out cacheValue)).Returns(true);
        
        mockMetadataStore.Setup(x => x.GetAsync<Dictionary<string, string>>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Should not query store on cache hit"));
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.Succeeded.Should().BeTrue();
        mockMetadataStore.Verify(x => x.GetAsync<Dictionary<string, string>>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithValidApiKeyAndCacheMiss_QueriesMetadataAndReturnsSuccess()
    {
        // Arrange
        var apiKey = "valid-key-456";
        var cacheKey = $"apikey:{apiKey}";
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = "t-xyz",
            ["active"] = "true",
            ["keyId"] = "key-456"
        };
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        var mockCacheEntry = new Mock<ICacheEntry>();
        
        object? nullValue = null;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out nullValue)).Returns(false);
        mockCache.Setup(x => x.CreateEntry(cacheKey)).Returns(mockCacheEntry.Object);
        
        mockMetadataStore.Setup(x => x.GetAsync<Dictionary<string, string>>("api_keys", apiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.Succeeded.Should().BeTrue();
        mockMetadataStore.Verify(x => x.GetAsync<Dictionary<string, string>>("api_keys", apiKey, It.IsAny<CancellationToken>()), Times.Once);
        mockCache.Verify(x => x.CreateEntry(cacheKey), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidApiKeyOnCacheHit_SetsCorrectTenantIdClaim()
    {
        // Arrange
        var apiKey = "valid-key-789";
        var cacheKey = $"apikey:{apiKey}";
        var expectedTenantId = "t-tenant-123";
        var cachedValue = (expectedTenantId, "key-789");
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        
        object? cacheValue = cachedValue;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out cacheValue)).Returns(true);
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.FindFirst("tenantId")?.Value.Should().Be(expectedTenantId);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidApiKey_ReturnsFail()
    {
        // Arrange
        var apiKey = "invalid-key-999";
        var cacheKey = $"apikey:{apiKey}";
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        
        object? nullValue = null;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out nullValue)).Returns(false);
        
        mockMetadataStore.Setup(x => x.GetAsync<Dictionary<string, string>>("api_keys", apiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure?.Message.Should().Contain("Invalid API key");
    }

    [Fact]
    public async Task HandleAsync_WithInactiveApiKey_ReturnsFail()
    {
        // Arrange
        var apiKey = "inactive-key-111";
        var cacheKey = $"apikey:{apiKey}";
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = "t-disabled",
            ["active"] = "false",
            ["keyId"] = "key-111"
        };
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        
        object? nullValue = null;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out nullValue)).Returns(false);
        mockMetadataStore.Setup(x => x.GetAsync<Dictionary<string, string>>("api_keys", apiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure?.Message.Should().Contain("inactive");
    }

    [Fact]
    public async Task HandleAsync_WithMissingHeader_ReturnsNoResult()
    {
        // Arrange
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, null);
        
        // Act
        var result = await handler.AuthenticateAsync();
        
        // Assert
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithValidKey_CachesResultForConfiguredDuration()
    {
        // Arrange
        var apiKey = "cache-test-key";
        var cacheKey = $"apikey:{apiKey}";
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = "t-cached",
            ["active"] = "true",
            ["keyId"] = "cache-key"
        };
        
        var mockMetadataStore = new Mock<IMetadataStore>();
        var mockCache = new Mock<IMemoryCache>();
        var mockCacheEntry = new Mock<ICacheEntry>();
        
        object? nullValue = null;
        mockCache.Setup(x => x.TryGetValue(cacheKey, out nullValue)).Returns(false);
        mockCache.Setup(x => x.CreateEntry(cacheKey)).Returns(mockCacheEntry.Object);
        
        mockMetadataStore.Setup(x => x.GetAsync<Dictionary<string, string>>("api_keys", apiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        
        var handler = CreateHandler(mockMetadataStore.Object, mockCache.Object, apiKey);
        
        // Act
        await handler.AuthenticateAsync();
        
        // Assert
        mockCache.Verify(x => x.CreateEntry(cacheKey), Times.Once);
        mockCacheEntry.VerifySet(x => x.Value = It.Is<(string, string)>(v => v.Item1 == "t-cached" && v.Item2 == "cache-key"), Times.Once);
    }

    private static ApiKeyAuthHandler CreateHandler(IMetadataStore metadataStore, IMemoryCache cache, string? apiKey)
    {
        var options = new ApiKeyAuthOptions { HeaderName = "X-Api-Key", CacheDurationSeconds = 300 };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ApiKeyAuthOptions>>(o => o.CurrentValue == options && o.Get(It.IsAny<string>()) == options);
        var loggerFactory = LoggerFactory.Create(builder => { });
        var encoder = UrlEncoder.Default;
        var logger = Mock.Of<ILogger<ApiKeyAuthHandler>>();
        
        var handler = new ApiKeyAuthHandler(optionsMonitor, loggerFactory, encoder, cache, metadataStore, logger);
        
        var context = new DefaultHttpContext();
        if (apiKey != null)
        {
            context.Request.Headers["X-Api-Key"] = apiKey;
        }
        
        var scheme = new AuthenticationScheme(ApiKeyAuthOptions.SchemeName, null, typeof(ApiKeyAuthHandler));
        handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();
        
        return handler;
    }
}
