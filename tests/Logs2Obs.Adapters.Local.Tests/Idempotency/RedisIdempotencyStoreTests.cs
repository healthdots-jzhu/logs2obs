using FluentAssertions;
using Logs2Obs.Adapters.Local.Idempotency;
using Logs2Obs.Adapters.Local.Options;
using Logs2Obs.Adapters.Local.Tests.Fixtures;

namespace Logs2Obs.Adapters.Local.Tests.Idempotency;

using Options = Microsoft.Extensions.Options.Options;

[Collection("Redis")]
public class RedisIdempotencyStoreTests(RedisFixture fixture)
{
    private RedisIdempotencyStore CreateSut() =>
        new(Options.Create(new RedisOptions
        {
            ConnectionString   = fixture.ConnectionString,
            InstanceName       = "idempotency-test:",
            DefaultTtlSeconds  = 60,
        }));

    [Fact]
    public async Task CheckAndSetAsync_FirstCall_ReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        var key = Guid.NewGuid().ToString();

        // Act
        var result = await sut.CheckAndSetAsync(key, TimeSpan.FromSeconds(60));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAndSetAsync_SecondCallSameKey_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var key = Guid.NewGuid().ToString();
        await sut.CheckAndSetAsync(key, TimeSpan.FromSeconds(60));

        // Act
        var duplicate = await sut.CheckAndSetAsync(key, TimeSpan.FromSeconds(60));

        // Assert
        duplicate.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAndSetAsync_AfterExpireAsync_ReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        var key = Guid.NewGuid().ToString();
        await sut.CheckAndSetAsync(key, TimeSpan.FromSeconds(60));

        // Act
        await sut.ExpireAsync(key);
        var afterExpire = await sut.CheckAndSetAsync(key, TimeSpan.FromSeconds(60));

        // Assert
        afterExpire.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAndSetAsync_DifferentKeys_BothReturnTrue()
    {
        // Arrange
        var sut = CreateSut();
        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        // Act
        var result1 = await sut.CheckAndSetAsync(key1, TimeSpan.FromSeconds(60));
        var result2 = await sut.CheckAndSetAsync(key2, TimeSpan.FromSeconds(60));

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }
}
