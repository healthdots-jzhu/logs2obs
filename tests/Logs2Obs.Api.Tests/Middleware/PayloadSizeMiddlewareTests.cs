using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Logs2Obs.Api.Middleware;
using Logs2Obs.Api.Options;

namespace Logs2Obs.Api.Tests.Middleware;

/// <summary>
/// Tests for PayloadSizeMiddleware (to be created by Maeve in Phase 4).
/// Tests request body size limits (default 500KB, configurable per endpoint).
/// </summary>
public class PayloadSizeMiddlewareTests
{
    [Fact]
    public async Task RequestUnderLimit_PassesThrough()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/logs";
        var smallPayload = new byte[100];
        context.Request.Body = new MemoryStream(smallPayload);
        context.Request.ContentLength = smallPayload.Length;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Microsoft.Extensions.Options.Options.Create(new PayloadSizeOptions { MaxPayloadBytes = 500_000 });
        var logger = Mock.Of<ILogger<PayloadSizeMiddleware>>();
        var middleware = new PayloadSizeMiddleware(next, options, logger);

        // Act
        await middleware.InvokeAsync(context, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task RequestOverLimit_Returns413()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/logs";
        context.Response.Body = new MemoryStream();
        var largePayload = new byte[600_000];
        context.Request.Body = new MemoryStream(largePayload);
        context.Request.ContentLength = largePayload.Length;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Microsoft.Extensions.Options.Options.Create(new PayloadSizeOptions { MaxPayloadBytes = 500_000 });
        var logger = Mock.Of<ILogger<PayloadSizeMiddleware>>();
        var middleware = new PayloadSizeMiddleware(next, options, logger);

        // Act
        await middleware.InvokeAsync(context, CancellationToken.None);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task ExactlyAtLimit_PassesThrough()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/logs";
        var exactPayload = new byte[500_000];
        context.Request.Body = new MemoryStream(exactPayload);
        context.Request.ContentLength = exactPayload.Length;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Microsoft.Extensions.Options.Options.Create(new PayloadSizeOptions { MaxPayloadBytes = 500_000 });
        var logger = Mock.Of<ILogger<PayloadSizeMiddleware>>();
        var middleware = new PayloadSizeMiddleware(next, options, logger);

        // Act
        await middleware.InvokeAsync(context, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task BulkUploadEndpoint_NotSubjectToSizeLimit()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/bulk-upload";
        var largePayload = new byte[10_000_000];
        context.Request.Body = new MemoryStream(largePayload);
        context.Request.ContentLength = largePayload.Length;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var options = Microsoft.Extensions.Options.Options.Create(new PayloadSizeOptions { MaxPayloadBytes = 500_000 });
        var logger = Mock.Of<ILogger<PayloadSizeMiddleware>>();
        var middleware = new PayloadSizeMiddleware(next, options, logger);

        // Act
        await middleware.InvokeAsync(context, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
