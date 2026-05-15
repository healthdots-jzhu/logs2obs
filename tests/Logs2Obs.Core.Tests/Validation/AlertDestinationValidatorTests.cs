using Logs2Obs.Core.Validation;

namespace Logs2Obs.Core.Tests.Validation;

public class AlertDestinationValidatorTests
{
    private readonly AlertDestinationValidator _validator = new();

    [Fact]
    public void Validate_WithHttpsWebhook_ReturnsValid()
    {
        var destination = new AlertDestination
        {
            Type = "webhook",
            WebhookUrl = "https://hooks.example.test/alerts"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithSlackHook_ReturnsValid()
    {
        var destination = new AlertDestination
        {
            Type = "slack",
            WebhookUrl = "https://hooks.slack.test/services/T000/B000/secret"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMissingWebhookUrl_ReturnsInvalid()
    {
        var destination = new AlertDestination
        {
            Type = "webhook",
            WebhookUrl = null
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithUnsupportedHookType_ReturnsInvalid()
    {
        var destination = new AlertDestination
        {
            Type = "pagerduty",
            WebhookUrl = "https://events.pagerduty.test/integration/key/enqueue"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithPlainHttpExternalUrl_ReturnsInvalid()
    {
        var destination = new AlertDestination
        {
            Type = "webhook",
            WebhookUrl = "http://hooks.example.test/alerts"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithPlainHttpLocalhostUrl_ReturnsValid()
    {
        var destination = new AlertDestination
        {
            Type = "webhook",
            WebhookUrl = "http://localhost:5050/alerts"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithUrlFragment_ReturnsInvalid()
    {
        var destination = new AlertDestination
        {
            Type = "webhook",
            WebhookUrl = "https://hooks.example.test/alerts#token"
        };

        var result = _validator.Validate(destination);

        result.IsValid.Should().BeFalse();
    }
}
