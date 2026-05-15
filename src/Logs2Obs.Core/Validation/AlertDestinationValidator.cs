namespace Logs2Obs.Core.Validation;

using FluentValidation;
using Logs2Obs.Core.Models;

public sealed class AlertDestinationValidator : AbstractValidator<AlertDestination>
{
    private static readonly HashSet<string> SupportedHookTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "slack",
        "webhook"
    };

    public AlertDestinationValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .MaximumLength(32)
            .Must(SupportedHookTypes.Contains)
            .WithMessage("Destination type must be one of: slack, webhook.");

        RuleFor(x => x.WebhookUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(BeValidWebhookUrl)
            .WithMessage("WebhookUrl must be an absolute HTTPS URL, or HTTP for localhost development.");

        RuleFor(x => x.IntegrationKey)
            .MaximumLength(4096)
            .When(x => x.IntegrationKey is not null);
    }

    private static bool BeValidWebhookUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }
}
