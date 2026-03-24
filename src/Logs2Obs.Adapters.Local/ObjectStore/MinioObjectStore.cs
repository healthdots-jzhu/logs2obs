namespace Logs2Obs.Adapters.Local.ObjectStore;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

public sealed class MinioObjectStore(
    IOptions<MinioOptions> options,
    ILogger<MinioObjectStore>? logger = null) : IObjectStore
{
    private static IMinioClient BuildClient(MinioOptions opts) =>
        new MinioClient()
            .WithEndpoint(opts.Endpoint)
            .WithCredentials(opts.AccessKey, opts.SecretKey)
            .WithSSL(opts.UseSSL)
            .Build();

    private readonly IMinioClient _client = BuildClient(options.Value);
    private readonly MinioOptions _opts = options.Value;
    private readonly ILogger<MinioObjectStore> _logger =
        logger ?? NullLogger<MinioObjectStore>.Instance;

    public async Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            await EnsureBucketAsync(token);
            var args = new PutObjectArgs()
                .WithBucket(_opts.BucketName)
                .WithObject(key)
                .WithStreamData(content)
                .WithObjectSize(content.CanSeek ? content.Length : -1)
                .WithContentType(contentType);
            await _client.PutObjectAsync(args, token);
            return true;
        }, ct);
        _logger.LogDebug("Wrote object {Key} to MinIO bucket {Bucket}", key, _opts.BucketName);
    }

    public async Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<Stream?>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var ms = new MemoryStream();
            try
            {
                var args = new GetObjectArgs()
                    .WithBucket(_opts.BucketName)
                    .WithObject(key)
                    .WithCallbackStream(async (stream, t) => await stream.CopyToAsync(ms, t));
                await _client.GetObjectAsync(args, token);
                ms.Position = 0;
                return (Stream?)ms;
            }
            catch (ObjectNotFoundException)
            {
                await ms.DisposeAsync();
                return null;
            }
            catch (BucketNotFoundException)
            {
                await ms.DisposeAsync();
                return null;
            }
        }, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var args = new RemoveObjectArgs()
                .WithBucket(_opts.BucketName)
                .WithObject(key);
            await _client.RemoveObjectAsync(args, token);
            return true;
        }, ct);
        _logger.LogDebug("Deleted object {Key} from MinIO bucket {Bucket}", key, _opts.BucketName);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForStorage<bool>();
        return await pipeline.ExecuteAsync(async token =>
        {
            try
            {
                var args = new StatObjectArgs()
                    .WithBucket(_opts.BucketName)
                    .WithObject(key);
                await _client.StatObjectAsync(args, token);
                return true;
            }
            catch (ObjectNotFoundException)
            {
                return false;
            }
            catch (BucketNotFoundException)
            {
                return false;
            }
        }, ct);
    }

    public async IAsyncEnumerable<string> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = new ListObjectsArgs()
            .WithBucket(_opts.BucketName)
            .WithPrefix(prefix)
            .WithRecursive(true);

        await foreach (var item in _client.ListObjectsEnumAsync(args, ct))
        {
            yield return item.Key;
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        var existsArgs = new BucketExistsArgs().WithBucket(_opts.BucketName);
        bool exists = await _client.BucketExistsAsync(existsArgs, ct);
        if (!exists)
        {
            var makeArgs = new MakeBucketArgs().WithBucket(_opts.BucketName);
            await _client.MakeBucketAsync(makeArgs, ct);
            _logger.LogInformation("Created MinIO bucket {Bucket}", _opts.BucketName);
        }
    }
}
