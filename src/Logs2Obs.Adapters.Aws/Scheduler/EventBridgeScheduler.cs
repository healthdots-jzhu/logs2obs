namespace Logs2Obs.Adapters.Aws.Scheduler;

using System.Globalization;
using System.Text.Json;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

public sealed class EventBridgeScheduler(
    IAmazonScheduler scheduler,
    IOptions<AwsAdaptersOptions> options) : Logs2Obs.Core.Abstractions.IScheduler
{
    private readonly EventBridgeOptions _opts = options.Value.EventBridge;

    public async Task ScheduleAsync(string jobId, string cronExpression, string jobType, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var payload = JsonSerializer.Serialize(new { jobId, jobType });
            var request = new CreateScheduleRequest
            {
                Name = jobId,
                GroupName = _opts.ScheduleGroupName,
                ScheduleExpression = NormalizeCron(cronExpression),
                Target = new Target
                {
                    Arn = _opts.TargetArn,
                    RoleArn = _opts.RoleArn,
                    Input = payload
                },
                FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF }
            };

            await scheduler.CreateScheduleAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    }

    public async Task UnscheduleAsync(string jobId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var request = new DeleteScheduleRequest
            {
                Name = jobId,
                GroupName = _opts.ScheduleGroupName
            };
            await scheduler.DeleteScheduleAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetNextRunTimeAsync(string jobId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<DateTimeOffset?>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var request = new GetScheduleRequest
            {
                Name = jobId,
                GroupName = _opts.ScheduleGroupName
            };

            var response = await scheduler.GetScheduleAsync(request, token).ConfigureAwait(false);
            var expression = response.ScheduleExpression;
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            if (expression.StartsWith("rate(", StringComparison.OrdinalIgnoreCase))
                return ComputeNextFromRate(expression);

            return null;
        }, ct).ConfigureAwait(false);
    }

    private static string NormalizeCron(string cronExpression)
    {
        if (cronExpression.StartsWith("cron(", StringComparison.OrdinalIgnoreCase))
            return cronExpression;

        return $"cron({cronExpression})";
    }

    private static DateTimeOffset? ComputeNextFromRate(string expression)
    {
        var trimmed = expression.Trim();
        if (!trimmed.StartsWith("rate(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
            return null;

        var inner = trimmed[5..^1].Trim();
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;

        var unit = parts[1].ToLowerInvariant();
        return unit switch
        {
            "minute" or "minutes" => DateTimeOffset.UtcNow.AddMinutes(value),
            "hour" or "hours" => DateTimeOffset.UtcNow.AddHours(value),
            "day" or "days" => DateTimeOffset.UtcNow.AddDays(value),
            _ => null
        };
    }
}
