using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.OpenSearchService;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using AlbHealthCheck = Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck;
using EcrLifecycleRule = Amazon.CDK.AWS.ECR.LifecycleRule;

namespace Logs2Obs.Cdk.Stacks;

public class ComputeStackProps : StackProps
{
    public IVpc Vpc { get; set; } = null!;
    public ApplicationListener HttpsListener { get; set; } = null!;
    public SecurityGroup EcsSecurityGroup { get; set; } = null!;
    public Bucket RawBucket { get; set; } = null!;
    public Bucket AthenaResultsBucket { get; set; } = null!;
    public Topic IngestTopic { get; set; } = null!;
    public Topic EventsTopic { get; set; } = null!;
    public Queue WorkerIngestQueue { get; set; } = null!;
    public Queue WorkerAlertsQueue { get; set; } = null!;
    public Queue WorkerMatviewQueue { get; set; } = null!;
    public Queue WorkerReplayQueue { get; set; } = null!;
    public Queue PullerScheduleQueue { get; set; } = null!;
    public Queue QueryAsyncQueue { get; set; } = null!;
    public Queue ApiWebhookQueue { get; set; } = null!;
    public Queue DeadletterAuditQueue { get; set; } = null!;
    public Table TenantsTable { get; set; } = null!;
    public Table PullJobsTable { get; set; } = null!;
    public Table SavedQueriesTable { get; set; } = null!;
    public Table AlertRulesTable { get; set; } = null!;
    public Table ReplayJobsTable { get; set; } = null!;
    public Table QueryExecutionsTable { get; set; } = null!;
    public Table SchemaVersionsTable { get; set; } = null!;
    public Table MetadataTable { get; set; } = null!;
    public Domain SearchDomain { get; set; } = null!;
    public string RedisEndpoint { get; set; } = string.Empty;
}

public class ComputeStack : Stack
{
    public Cluster Cluster { get; }
    public SecurityGroup EcsSecurityGroup { get; }
    public Role TaskRole { get; }
    public Repository ApiRepository { get; }
    public Repository WorkerRepository { get; }
    public Repository PullerRepository { get; }
    public Repository QueryEngineRepository { get; }

    public ComputeStack(Construct scope, string id, ComputeStackProps props)
        : base(scope, id, props)
    {
        EcsSecurityGroup = props.EcsSecurityGroup;

        ApiRepository = CreateRepository("ApiRepository", "logs2obs-api");
        WorkerRepository = CreateRepository("WorkerRepository", "logs2obs-worker");
        PullerRepository = CreateRepository("PullerRepository", "logs2obs-puller");
        QueryEngineRepository = CreateRepository("QueryEngineRepository", "logs2obs-queryengine");

        Cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            ClusterName = "logs2obs-cluster",
            Vpc = props.Vpc
        });

        var taskExecutionRole = new Role(this, "TaskExecutionRole", new RoleProps
        {
            RoleName = "logs2obs-task-execution",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com")
        });

        taskExecutionRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy"));
        taskExecutionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "secretsmanager:GetSecretValue" },
            Resources = new[] { $"arn:aws:secretsmanager:{Stack.Of(this).Region}:{Stack.Of(this).Account}:secret:logs2obs/*" }
        }));

        TaskRole = new Role(this, "TaskRole", new RoleProps
        {
            RoleName = "logs2obs-task-role",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com")
        });

        props.RawBucket.GrantReadWrite(TaskRole);
        props.AthenaResultsBucket.GrantReadWrite(TaskRole);

        props.IngestTopic.GrantPublish(TaskRole);
        props.EventsTopic.GrantPublish(TaskRole);

        props.TenantsTable.GrantReadWriteData(TaskRole);
        props.PullJobsTable.GrantReadWriteData(TaskRole);
        props.SavedQueriesTable.GrantReadWriteData(TaskRole);
        props.AlertRulesTable.GrantReadWriteData(TaskRole);
        props.ReplayJobsTable.GrantReadWriteData(TaskRole);
        props.QueryExecutionsTable.GrantReadWriteData(TaskRole);
        props.SchemaVersionsTable.GrantReadWriteData(TaskRole);
        props.MetadataTable.GrantReadWriteData(TaskRole);

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[]
            {
                "sqs:SendMessage",
                "sqs:SendMessageBatch",
                "sqs:ReceiveMessage",
                "sqs:DeleteMessage",
                "sqs:DeleteMessageBatch",
                "sqs:ChangeMessageVisibility",
                "sqs:ChangeMessageVisibilityBatch",
                "sqs:GetQueueAttributes",
                "sqs:GetQueueUrl"
            },
            Resources = new[]
            {
                props.WorkerIngestQueue.QueueArn,
                props.WorkerAlertsQueue.QueueArn,
                props.WorkerMatviewQueue.QueueArn,
                props.WorkerReplayQueue.QueueArn,
                props.PullerScheduleQueue.QueueArn,
                props.QueryAsyncQueue.QueueArn,
                props.ApiWebhookQueue.QueueArn,
                props.DeadletterAuditQueue.QueueArn
            }
        }));

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "es:ESHttp*" },
            Resources = new[] { $"{props.SearchDomain.DomainArn}/*" }
        }));

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "athena:*" },
            Resources = new[] { $"arn:aws:athena:{Stack.Of(this).Region}:{Stack.Of(this).Account}:workgroup/*" }
        }));

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "glue:*" },
            Resources = new[]
            {
                $"arn:aws:glue:{Stack.Of(this).Region}:{Stack.Of(this).Account}:catalog",
                $"arn:aws:glue:{Stack.Of(this).Region}:{Stack.Of(this).Account}:database/logs2obs",
                $"arn:aws:glue:{Stack.Of(this).Region}:{Stack.Of(this).Account}:table/logs2obs/*"
            }
        }));

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "secretsmanager:GetSecretValue" },
            Resources = new[] { $"arn:aws:secretsmanager:{Stack.Of(this).Region}:{Stack.Of(this).Account}:secret:logs2obs/*" }
        }));

        TaskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "elasticache:Connect" },
            Resources = new[] { $"arn:aws:elasticache:{Stack.Of(this).Region}:{Stack.Of(this).Account}:replicationgroup:logs2obs-redis" }
        }));

        var sharedEnv = new Dictionary<string, string>
        {
            ["AWS_REGION"] = Stack.Of(this).Region,
            ["S3_RAW_BUCKET"] = props.RawBucket.BucketName,
            ["S3_ATHENA_RESULTS_BUCKET"] = props.AthenaResultsBucket.BucketName,
            ["DYNAMODB_METADATA_TABLE"] = "logs2obs-metadata",
            ["REDIS_ENDPOINT"] = props.RedisEndpoint,
            ["INGEST_TOPIC_ARN"] = props.IngestTopic.TopicArn,
            ["EVENTS_TOPIC_ARN"] = props.EventsTopic.TopicArn
        };

        var apiTaskDefinition = CreateTaskDefinition("ApiTaskDefinition", 512, 1024, taskExecutionRole);
        _ = apiTaskDefinition.AddContainer("ApiContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(ApiRepository, "latest"),
            Environment = new Dictionary<string, string>(sharedEnv),
            PortMappings = new[] { new PortMapping { ContainerPort = 8080 } }
        });

        var apiService = new FargateService(this, "ApiService", new FargateServiceProps
        {
            Cluster = Cluster,
            TaskDefinition = apiTaskDefinition,
            DesiredCount = 2,
            AssignPublicIp = false,
            SecurityGroups = new[] { props.EcsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });

        _ = props.HttpsListener.AddTargets("ApiTargets", new AddApplicationTargetsProps
        {
            Port = 8080,
            Targets = new[] { apiService },
            HealthCheck = new AlbHealthCheck { Path = "/health" }
        });

        var workerTaskDefinition = CreateTaskDefinition("WorkerTaskDefinition", 256, 512, taskExecutionRole);
        _ = workerTaskDefinition.AddContainer("WorkerContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(WorkerRepository, "latest"),
            Environment = new Dictionary<string, string>(sharedEnv)
        });

        _ = new FargateService(this, "WorkerService", new FargateServiceProps
        {
            Cluster = Cluster,
            TaskDefinition = workerTaskDefinition,
            DesiredCount = 1,
            AssignPublicIp = false,
            SecurityGroups = new[] { props.EcsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });

        var pullerTaskDefinition = CreateTaskDefinition("PullerTaskDefinition", 256, 512, taskExecutionRole);
        _ = pullerTaskDefinition.AddContainer("PullerContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(PullerRepository, "latest"),
            Environment = new Dictionary<string, string>(sharedEnv)
        });

        _ = new FargateService(this, "PullerService", new FargateServiceProps
        {
            Cluster = Cluster,
            TaskDefinition = pullerTaskDefinition,
            DesiredCount = 1,
            AssignPublicIp = false,
            SecurityGroups = new[] { props.EcsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });

        var queryTaskDefinition = CreateTaskDefinition("QueryTaskDefinition", 256, 512, taskExecutionRole);
        _ = queryTaskDefinition.AddContainer("QueryContainer", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(QueryEngineRepository, "latest"),
            Environment = new Dictionary<string, string>(sharedEnv),
            PortMappings = new[] { new PortMapping { ContainerPort = 8080 } }
        });

        var queryService = new FargateService(this, "QueryService", new FargateServiceProps
        {
            Cluster = Cluster,
            TaskDefinition = queryTaskDefinition,
            DesiredCount = 1,
            AssignPublicIp = false,
            SecurityGroups = new[] { props.EcsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });

        _ = props.HttpsListener.AddTargets("QueryTargets", new AddApplicationTargetsProps
        {
            Port = 8080,
            Targets = new[] { queryService },
            Priority = 10,
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/query*", "/query/*" }) }
        });
    }

    private Repository CreateRepository(string id, string name)
    {
        return new Repository(this, id, new RepositoryProps
        {
            RepositoryName = name,
            LifecycleRules = new[] { new EcrLifecycleRule { MaxImageCount = 10 } }
        });
    }

    private FargateTaskDefinition CreateTaskDefinition(string id, int cpu, int memory, Role executionRole)
    {
        return new FargateTaskDefinition(this, id, new FargateTaskDefinitionProps
        {
            Cpu = cpu,
            MemoryLimitMiB = memory,
            ExecutionRole = executionRole,
            TaskRole = TaskRole
        });
    }
}
