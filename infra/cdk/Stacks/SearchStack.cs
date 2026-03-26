using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.OpenSearchService;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class SearchStack : Stack
{
    public Domain Domain { get; }

    public SearchStack(Construct scope, string id, StackProps props)
        : base(scope, id, props)
    {
        var account = Stack.Of(this).Account;
        var region = Stack.Of(this).Region;
        var taskRoleArn = $"arn:aws:iam::{account}:role/logs2obs-task-role";

        var accessPolicy = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[] { "es:ESHttp*" },
            Principals = new[] { new ArnPrincipal(taskRoleArn) },
            Resources = new[] { $"arn:aws:es:{region}:{account}:domain/logs2obs/*" }
        });

        Domain = new Domain(this, "OpenSearchDomain", new DomainProps
        {
            DomainName = "logs2obs",
            Version = EngineVersion.OPENSEARCH_2_11,
            Capacity = new CapacityConfig
            {
                DataNodes = 1,
                DataNodeInstanceType = "t3.small.search"
            },
            Ebs = new EbsOptions
            {
                Enabled = true,
                VolumeSize = 100,
                VolumeType = EbsDeviceVolumeType.GP3
            },
            EncryptionAtRest = new EncryptionAtRestOptions { Enabled = true },
            NodeToNodeEncryption = true,
            EnforceHttps = true,
            AccessPolicies = new[] { accessPolicy }
        });

        // ISM policy: infra/opensearch/ilm-policy.json
        // Index template: infra/opensearch/index-template.json
    }
}
