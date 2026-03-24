using FluentAssertions;
using Logs2Obs.Core.Mapping;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Tests.Helpers;

namespace Logs2Obs.Core.Tests.Mapping;

public class DtoMapperTests
{
    [Fact]
    public void ToDomain_Always_GeneratesNewId()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var result = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void ToDomain_Always_SetsTenantIdFromParameter_NotFromDto()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var result = DtoMapper.ToDomain(dto, "t-expected", IngestionMode.Push);

        result.TenantId.Should().Be("t-expected");
    }

    [Fact]
    public void ToDomain_Always_SetsIngestedAtToApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow;
        var result = DtoMapper.ToDomain(TestDataBuilders.AValidLogEntryDto(), "t-test", IngestionMode.Push);
        var after = DateTimeOffset.UtcNow;

        result.IngestedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void ToDomain_Always_SetsIngestionModeFromParameter()
    {
        var result = DtoMapper.ToDomain(TestDataBuilders.AValidLogEntryDto(), "t-test", IngestionMode.Pull);

        result.IngestionMode.Should().Be(IngestionMode.Pull);
    }

    [Fact]
    public void ToDomain_CalledTwice_ProducesDifferentIds()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var r1 = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);
        var r2 = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        r1.Id.Should().NotBe(r2.Id);
    }

    [Fact]
    public void ToDto_RoundTrip_PreservesSourceIdAndMessage()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        var domain = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        var result = DtoMapper.ToDto(domain);

        result.SourceId.Should().Be(dto.SourceId);
        result.Message.Should().Be(dto.Message);
    }

    [Fact]
    public void ToDomain_NeverUsesIdFromDto()
    {
        // The DTO's Id field is always ignored — the system generates a fresh one
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Id = "caller-supplied-id-should-be-ignored";

        var result = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        result.Id.Should().NotBe("caller-supplied-id-should-be-ignored");
    }

    [Fact]
    public void ToDomain_MapsSourceIdFromDto()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var result = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        result.SourceId.Should().Be(dto.SourceId);
    }

    [Fact]
    public void ToDomain_MapsMessageFromDto()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var result = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        result.Message.Should().Be(dto.Message);
    }

    [Fact]
    public void ToDomain_MapsTimestampFromDto()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();

        var result = DtoMapper.ToDomain(dto, "t-test", IngestionMode.Push);

        result.Timestamp.Should().Be(dto.Timestamp);
    }

    [Fact]
    public void ToDomain_AllIngestionModes_ArePreserved()
    {
        foreach (var mode in Enum.GetValues<IngestionMode>())
        {
            var result = DtoMapper.ToDomain(TestDataBuilders.AValidLogEntryDto(), "t-test", mode);
            result.IngestionMode.Should().Be(mode);
        }
    }
}
