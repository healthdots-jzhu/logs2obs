using Logs2Obs.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using FluentValidation;

namespace Logs2Obs.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        Log.Error(exception, "Unhandled exception: CorrelationId={CorrelationId}, Path={Path}", 
            correlationId, httpContext.Request.Path);

        var (statusCode, error) = exception switch
        {
            FluentValidation.ValidationException ve => (StatusCodes.Status400BadRequest, new
            {
                error = "Validation failed",
                correlationId,
                validationErrors = ve.Errors.Select(e => new { e.PropertyName, e.ErrorMessage })
            }),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, new
            {
                error = "Unauthorized",
                correlationId
            }),
            Logs2ObsException le => MapLogs2ObsException(le, correlationId),
            _ => (StatusCodes.Status500InternalServerError, new
            {
                error = "Internal server error",
                correlationId
            })
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
        
        return true;
    }

    private static (int statusCode, object error) MapLogs2ObsException(Logs2ObsException ex, string correlationId)
    {
        return ex switch
        {
            SqlSafetyException => (StatusCodes.Status400BadRequest, new
            {
                error = ex.Message,
                correlationId
            }),
            QueryGuardException => (StatusCodes.Status400BadRequest, new
            {
                error = ex.Message,
                correlationId
            }),
            TenantNotFoundException => (StatusCodes.Status404NotFound, new
            {
                error = ex.Message,
                correlationId
            }),
            _ => (StatusCodes.Status500InternalServerError, new
            {
                error = "Internal server error",
                correlationId
            })
        };
    }
}
