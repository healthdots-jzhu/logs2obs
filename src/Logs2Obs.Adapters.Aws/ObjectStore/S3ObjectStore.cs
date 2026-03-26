namespace Logs2Obs.Adapters.Aws.ObjectStore;

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

public sealed class S3ObjectStore(
    IAmazonS3 s3,
    IOptions<AwsAdaptersOptions> options) : IObjectStore
{
    private readonly S3Options _opts = options.Value.S3;

    private static string BuildKey(string prefix, string key)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return key;
        var trimmed = prefix.TrimEnd('/');
        var keyTrimmed = key.TrimStart('/');
        return $"{trimmed}/{keyTrimmed}";
    }

    public async Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var request = new PutObjectRequest
            {
                BucketName = _opts.BucketName,
                Key = BuildKey(_opts.KeyPrefix, key),
                InputStream = content,
                ContentType = contentType
            };
            await s3.PutObjectAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    }

    public async Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<Stream?>();
        return await pipeline.ExecuteAsync(async token =>
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _opts.BucketName,
                    Key = BuildKey(_opts.KeyPrefix, key)
                };

                using var response = await s3.GetObjectAsync(request, token).ConfigureAwait(false);
                var ms = new MemoryStream();
                await response.ResponseStream.CopyToAsync(ms, token).ConfigureAwait(false);
                ms.Position = 0;
                return (Stream?)ms;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _opts.BucketName,
                Key = BuildKey(_opts.KeyPrefix, key)
            };
            await s3.DeleteObjectAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        return await pipeline.ExecuteAsync(async token =>
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _opts.BucketName,
                    Key = BuildKey(_opts.KeyPrefix, key)
                };
                await s3.GetObjectMetadataAsync(request, token).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<ListObjectsV2Response>();
        string? continuationToken = null;
        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _opts.BucketName,
                Prefix = BuildKey(_opts.KeyPrefix, prefix),
                ContinuationToken = continuationToken
            };

            var response = await pipeline.ExecuteAsync(
                async token => await s3.ListObjectsV2Async(request, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);

            foreach (var item in response.S3Objects)
            {
                yield return item.Key;
            }

            continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
        } while (!string.IsNullOrEmpty(continuationToken));
    }
}
