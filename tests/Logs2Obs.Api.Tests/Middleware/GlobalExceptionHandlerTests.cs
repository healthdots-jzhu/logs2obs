using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Logs2Obs.Api.Middleware;
using Logs2Obs.Core.Exceptions;
using FluentValidation;
using FluentValidation.Results;

namespace Logs2Obs.Api.Tests.Middleware;

/// <summary>
/// Tests for GlobalExceptionHandler (to be created by Maeve in Phase 4).
/// Tests error mapping: ValidationException → 400, UnauthorizedAccessException → 401,
/// unhandled exceptions → 500 with correlation ID (no stack trace leak).
/// </summary>
public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task ValidationException_Returns400WithErrors()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var validationFailures = new List<ValidationFailure>
        {
            new ValidationFailure("TenantId", "TenantId is required"),
            new ValidationFailure("Level", "Invalid log level")
        };
        var exception = new FluentValidation.ValidationException(validationFailures);
        
        var handler = new GlobalExceptionHandler();

        // Act
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        responseBody.Should().Contain("Validation failed");
        responseBody.Should().Contain("correlationId");
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns401()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-123";

        var exception = new UnauthorizedAccessException("Invalid API key");
        var handler = new GlobalExceptionHandler();

        // Act
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        responseBody.Should().Contain("Unauthorized");
        responseBody.Should().Contain("correlationId");
    }

    [Fact]
    public async Task UnhandledException_Returns500WithCorrelationId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "correlation-xyz";

        var exception = new InvalidOperationException("Unexpected database error");
        var handler = new GlobalExceptionHandler();

        // Act
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        responseBody.Should().Contain("correlationId");
        responseBody.Should().Contain("Internal server error");
    }

    [Fact]
    public async Task UnhandledException_DoesNotLeakStackTrace()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var exception = new InvalidOperationException("Internal error with sensitive details");
        var handler = new GlobalExceptionHandler();

        // Act
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        
        responseBody.Should().NotContain("at ");
        responseBody.Should().NotContain("System.");
        responseBody.Should().NotContain("Internal error with sensitive details");
        responseBody.Should().Contain("Internal server error");
    }
}
