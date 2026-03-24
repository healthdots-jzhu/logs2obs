namespace Logs2Obs.Core.Validation;

using FluentValidation;
using Logs2Obs.Core.Models;

/// <summary>FluentValidation validator for <see cref="LogEntryDto"/> following Section 27.9 rules.</summary>
public class LogEntryDtoValidator : AbstractValidator<LogEntryDto>
{
    public LogEntryDtoValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty()
            .MaximumLength(256)
            .Matches(@"^[a-zA-Z0-9\-_.:/]+$")
            .WithMessage("SourceId must contain only alphanumeric characters, hyphens, underscores, dots, colons, slashes.");

        RuleFor(x => x.LogType)
            .NotEmpty()
            .Must(v => Enum.TryParse<LogType>(v, ignoreCase: true, out _))
            .WithMessage("LogType must be one of: Application, Error, Network, OS, Metric, Audit, Custom.");

        RuleFor(x => x.Level)
            .NotEmpty()
            .Must(v => Enum.TryParse<LogLevel>(v, ignoreCase: true, out _))
            .WithMessage("Level must be one of: Trace, Debug, Information, Warning, Error, Fatal.");

        RuleFor(x => x.Environment)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Timestamp)
            .NotEmpty()
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .WithMessage("Timestamp cannot be more than 5 minutes in the future.")
            .GreaterThan(_ => DateTimeOffset.UtcNow.AddDays(-30))
            .WithMessage("Timestamp cannot be more than 30 days in the past.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(65536);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 50)
            .WithMessage("Maximum 50 tags per entry.")
            .Must(tags => tags == null || tags.Keys.All(k => k.Length <= 128))
            .WithMessage("Tag keys must be 128 characters or fewer.")
            .Must(tags => tags == null || tags.Values.All(v => v.Length <= 1024))
            .WithMessage("Tag values must be 1024 characters or fewer.");

        When(x => x.LogType?.Equals("Metric", StringComparison.OrdinalIgnoreCase) == true, () =>
        {
            RuleFor(x => x.Metric)
                .NotNull()
                .WithMessage("Metric entries must include a Metric payload.");

            RuleFor(x => x.Metric!.MetricName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(x => x.Metric!.Unit)
                .NotEmpty();
        });
    }
}
