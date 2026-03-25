using Logs2Obs.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logs2Obs.Api.Middleware;

public sealed class PayloadSizeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PayloadSizeOptions _options;
    private readonly ILogger<PayloadSizeMiddleware> _logger;

    public PayloadSizeMiddleware(
        RequestDelegate next,
        IOptions<PayloadSizeOptions> options,
        ILogger<PayloadSizeMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1/logs", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _options.MaxPayloadBytes)
            {
                _logger.LogWarning(
                    "Payload size {Size} exceeds limit {Limit} for path {Path}",
                    context.Request.ContentLength.Value,
                    _options.MaxPayloadBytes,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Payload too large",
                    maxSize = _options.MaxPayloadBytes,
                    receivedSize = context.Request.ContentLength.Value
                }, cancellationToken);
                return;
            }
        }

        await _next(context);
    }
}
