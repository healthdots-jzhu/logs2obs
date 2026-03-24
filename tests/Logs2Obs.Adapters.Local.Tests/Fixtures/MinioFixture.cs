using Testcontainers.Minio;

namespace Logs2Obs.Adapters.Local.Tests.Fixtures;

public class MinioFixture : IAsyncLifetime
{
    private readonly MinioContainer _container = new MinioBuilder()
        .Build();

    public string Endpoint => $"{_container.Hostname}:{_container.GetMappedPublicPort(9000)}";
    public string AccessKey => _container.GetAccessKey();
    public string SecretKey => _container.GetSecretKey();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
