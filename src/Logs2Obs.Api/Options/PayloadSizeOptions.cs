namespace Logs2Obs.Api.Options;

public sealed class PayloadSizeOptions
{
    public long MaxPayloadBytes { get; init; } = 10_485_760; // 10MB default
}
