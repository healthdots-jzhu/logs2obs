using Amazon.CDK;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class StorageStack : Stack
{
    public Bucket RawBucket { get; }
    public Bucket AthenaResultsBucket { get; }
    public CfnDatabase LogsDatabase { get; }

    public StorageStack(Construct scope, string id, StackProps props)
        : base(scope, id, props)
    {
        var account = Stack.Of(this).Account;

        RawBucket = new Bucket(this, "RawBucket", new BucketProps
        {
            BucketName = $"logs2obs-raw-{account}",
            Versioned = true,
            Encryption = BucketEncryption.S3_MANAGED,
            LifecycleRules = new[]
            {
                new LifecycleRule
                {
                    Transitions = new[]
                    {
                        new Transition
                        {
                            StorageClass = StorageClass.INFREQUENT_ACCESS,
                            TransitionAfter = Duration.Days(30)
                        },
                        new Transition
                        {
                            StorageClass = StorageClass.GLACIER,
                            TransitionAfter = Duration.Days(90)
                        }
                    },
                    Expiration = Duration.Days(365)
                }
            }
        });

        AthenaResultsBucket = new Bucket(this, "AthenaResultsBucket", new BucketProps
        {
            BucketName = $"logs2obs-athena-results-{account}",
            Encryption = BucketEncryption.S3_MANAGED,
            LifecycleRules = new[]
            {
                new LifecycleRule
                {
                    Expiration = Duration.Days(7)
                }
            }
        });

        LogsDatabase = new CfnDatabase(this, "LogsDatabase", new CfnDatabaseProps
        {
            CatalogId = account,
            DatabaseInput = new CfnDatabase.DatabaseInputProperty
            {
                Name = "logs2obs"
            }
        });

        _ = new CfnTable(this, "LogEntriesTable", new CfnTableProps
        {
            CatalogId = account,
            DatabaseName = LogsDatabase.Ref,
            TableInput = new CfnTable.TableInputProperty
            {
                Name = "log_entries",
                TableType = "EXTERNAL_TABLE",
                PartitionKeys = new[]
                {
                    new CfnTable.ColumnProperty { Name = "year", Type = "string" },
                    new CfnTable.ColumnProperty { Name = "month", Type = "string" },
                    new CfnTable.ColumnProperty { Name = "day", Type = "string" },
                    new CfnTable.ColumnProperty { Name = "source", Type = "string" }
                },
                StorageDescriptor = new CfnTable.StorageDescriptorProperty
                {
                    Columns = new[]
                    {
                        new CfnTable.ColumnProperty { Name = "timestamp", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "level", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "message", Type = "string" },
                        new CfnTable.ColumnProperty { Name = "attributes", Type = "string" }
                    },
                    Location = $"s3://{RawBucket.BucketName}/",
                    InputFormat = "org.apache.hadoop.hive.ql.io.parquet.MapredParquetInputFormat",
                    OutputFormat = "org.apache.hadoop.hive.ql.io.parquet.MapredParquetOutputFormat",
                    SerdeInfo = new CfnTable.SerdeInfoProperty
                    {
                        SerializationLibrary = "org.apache.hadoop.hive.ql.io.parquet.serde.ParquetHiveSerDe"
                    }
                }
            }
        });
    }
}
