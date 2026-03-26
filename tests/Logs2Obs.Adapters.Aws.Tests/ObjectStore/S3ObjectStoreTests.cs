namespace Logs2Obs.Adapters.Aws.Tests.ObjectStore;

using System.Linq;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Logs2Obs.Adapters.Aws.ObjectStore;
using Logs2Obs.Adapters.Aws.Options;
using Options = Microsoft.Extensions.Options.Options;

public sealed class S3ObjectStoreTests
{
    private readonly Mock<IAmazonS3> _s3 = new();
    private readonly string _bucketName = "unit-test-bucket";

    [Fact]
    public async Task ExistsAsync_WhenObjectExists_ReturnsTrue()
    {
        // Arrange
        var key = "exists/key.txt";
        SetupGetObjectMetadata(key, new GetObjectMetadataResponse());
        var sut = CreateSut();

        // Act
        var exists = await sut.ExistsAsync(key, CancellationToken.None);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenObjectNotFound_ReturnsFalse()
    {
        // Arrange
        var key = "missing/key.txt";
        var notFound = new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };
        SetupGetObjectMetadataException(key, notFound);
        var sut = CreateSut();

        // Act
        var exists = await sut.ExistsAsync(key, CancellationToken.None);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_CallsDeleteObject()
    {
        // Arrange
        var key = "delete/key.txt";
        _s3.Setup(s3 => s3.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        _s3.Setup(s3 => s3.DeleteObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        var sut = CreateSut();

        // Act
        await sut.DeleteAsync(key, CancellationToken.None);

        // Assert
        WasDeleteCalledFor(key).Should().BeTrue();
    }

    [Fact(Skip = "Requires AWS S3 for stream/multipart validation.")]
    public void WriteAsync_WhenCalled_UploadsObject()
    {
        _ = _bucketName;
    }

    [Fact(Skip = "Requires AWS S3 for stream download behavior.")]
    public void ReadAsync_WhenObjectExists_ReturnsStream()
    {
        _ = _bucketName;
    }

    [Fact(Skip = "Requires AWS S3 to validate listing pagination.")]
    public void ListAsync_WhenPrefixProvided_ReturnsKeys()
    {
        _ = _bucketName;
    }

    private S3ObjectStore CreateSut()
    {
        var options = Options.Create(new AwsAdaptersOptions
        {
            S3 = new S3Options { BucketName = _bucketName }
        });
        return new S3ObjectStore(_s3.Object, options);
    }

    private void SetupGetObjectMetadata(string key, GetObjectMetadataResponse response)
    {
        _s3.Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == _bucketName && r.Key == key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        _s3.Setup(s3 => s3.GetObjectMetadataAsync(
                _bucketName,
                key,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private void SetupGetObjectMetadataException(string key, AmazonS3Exception exception)
    {
        _s3.Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == _bucketName && r.Key == key),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        _s3.Setup(s3 => s3.GetObjectMetadataAsync(
                _bucketName,
                key,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private bool WasDeleteCalledFor(string key) =>
        _s3.Invocations.Any(invocation =>
        {
            if (invocation.Method.Name != nameof(IAmazonS3.DeleteObjectAsync))
                return false;

            if (invocation.Arguments.Count >= 2
                && invocation.Arguments[0] is string bucket
                && invocation.Arguments[1] is string objectKey)
            {
                return bucket == _bucketName && objectKey == key;
            }

            if (invocation.Arguments.Count >= 1
                && invocation.Arguments[0] is DeleteObjectRequest request)
            {
                return request.BucketName == _bucketName && request.Key == key;
            }

            return false;
        });
}
