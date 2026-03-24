namespace Logs2Obs.Adapters.Local.Options;

public sealed class MinioOptions
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string BucketName { get; set; } = "logs2obs";
    public bool UseSSL { get; set; } = false;
}
