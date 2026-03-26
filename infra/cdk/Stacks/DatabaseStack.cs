using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using DynamoAttribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace Logs2Obs.Cdk.Stacks;

public class DatabaseStack : Stack
{
    public Table TenantsTable { get; }
    public Table PullJobsTable { get; }
    public Table SavedQueriesTable { get; }
    public Table AlertRulesTable { get; }
    public Table ReplayJobsTable { get; }
    public Table QueryExecutionsTable { get; }
    public Table SchemaVersionsTable { get; }
    public Table MetadataTable { get; }

    public DatabaseStack(Construct scope, string id, StackProps props)
        : base(scope, id, props)
    {
        TenantsTable = CreateTable("TenantsTable", "logs2obs-tenants", "tenantId");

        PullJobsTable = CreateTable("PullJobsTable", "logs2obs-pull-jobs", "jobId");
        PullJobsTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "tenantId-index",
            PartitionKey = new DynamoAttribute { Name = "tenantId", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "createdAt", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        SavedQueriesTable = CreateTable("SavedQueriesTable", "logs2obs-saved-queries", "queryId");
        SavedQueriesTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "tenantId-index",
            PartitionKey = new DynamoAttribute { Name = "tenantId", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "createdAt", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        AlertRulesTable = CreateTable("AlertRulesTable", "logs2obs-alert-rules", "ruleId");
        AlertRulesTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "tenantId-index",
            PartitionKey = new DynamoAttribute { Name = "tenantId", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "createdAt", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        ReplayJobsTable = CreateTable("ReplayJobsTable", "logs2obs-replay-jobs", "executionId");

        QueryExecutionsTable = CreateTable("QueryExecutionsTable", "logs2obs-query-executions", "executionId");
        QueryExecutionsTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "tenantId-index",
            PartitionKey = new DynamoAttribute { Name = "tenantId", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "startedAt", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        SchemaVersionsTable = new Table(this, "SchemaVersionsTable", new TableProps
        {
            TableName = "logs2obs-schema-versions",
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            PartitionKey = new DynamoAttribute { Name = "schemaId", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "version", Type = AttributeType.STRING }
        });

        MetadataTable = new Table(this, "MetadataTable", new TableProps
        {
            TableName = "logs2obs-metadata",
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            PartitionKey = new DynamoAttribute { Name = "PK", Type = AttributeType.STRING },
            SortKey = new DynamoAttribute { Name = "SK", Type = AttributeType.STRING }
        });
    }

    private Table CreateTable(string id, string tableName, string partitionKey)
    {
        return new Table(this, id, new TableProps
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            PartitionKey = new DynamoAttribute { Name = partitionKey, Type = AttributeType.STRING }
        });
    }
}
