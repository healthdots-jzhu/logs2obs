using System.Text;
using FluentAssertions;
using Logs2Obs.Adapters.Local.ObjectStore;
using Logs2Obs.Adapters.Local.Options;
using Logs2Obs.Adapters.Local.Tests.Fixtures;

namespace Logs2Obs.Adapters.Local.Tests.ObjectStore;

using Options = Microsoft.Extensions.Options.Options;

[Collection("Minio")]
public class MinioObjectStoreTests(MinioFixture fixture)
{
    private MinioObjectStore CreateSut() =>
        new(Options.Create(new MinioOptions
        {
            Endpoint  = fixture.Endpoint,
            AccessKey = fixture.AccessKey,
            SecretKey = fixture.SecretKey,
            BucketName = "test-bucket",
            UseSSL    = false,
        }));

    private static Stream ToStream(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadStringAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_ReturnsOriginalContent()
    {
        // Arrange
        var sut = CreateSut();
        var key = $"test/{Guid.NewGuid()}";
        const string content = "hello logs2obs";

        // Act
        await sut.WriteAsync(key, ToStream(content), "text/plain");
        var result = await sut.ReadAsync(key);

        // Assert
        result.Should().NotBeNull();
        (await ReadStringAsync(result!)).Should().Be(content);
    }

    [Fact]
    public async Task ReadAsync_WhenKeyNotFound_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();
        var key = $"missing/{Guid.NewGuid()}";

        // Act
        var result = await sut.ReadAsync(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectPresent_ReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        var key = $"exists/{Guid.NewGuid()}";
        await sut.WriteAsync(key, ToStream("data"), "text/plain");

        // Act
        var exists = await sut.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectAbsent_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var key = $"absent/{Guid.NewGuid()}";

        // Act
        var exists = await sut.ExistsAsync(key);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WithPrefix_ReturnsMatchingKeys()
    {
        // Arrange
        var sut = CreateSut();
        var prefix = $"list/{Guid.NewGuid()}";
        var keys = new[] { $"{prefix}/a", $"{prefix}/b", $"{prefix}/c" };
        foreach (var k in keys)
            await sut.WriteAsync(k, ToStream("x"), "text/plain");

        // Act
        var listed = new List<string>();
        await foreach (var key in sut.ListAsync(prefix))
            listed.Add(key);

        // Assert
        listed.Should().BeEquivalentTo(keys);
    }

    [Fact]
    public async Task DeleteAsync_ThenExistsAsync_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var key = $"delete/{Guid.NewGuid()}";
        await sut.WriteAsync(key, ToStream("to-delete"), "text/plain");

        // Act
        await sut.DeleteAsync(key);
        var exists = await sut.ExistsAsync(key);

        // Assert
        exists.Should().BeFalse();
    }
}
