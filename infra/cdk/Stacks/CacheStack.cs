using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElastiCache;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class CacheStackProps : StackProps
{
    public IVpc Vpc { get; set; } = null!;
    public ISecurityGroup EcsSecurityGroup { get; set; } = null!;
}

public class CacheStack : Stack
{
    public string RedisEndpoint { get; }
    public string RedisPort { get; }

    public CacheStack(Construct scope, string id, CacheStackProps props)
        : base(scope, id, props)
    {
        var redisSecurityGroup = new SecurityGroup(this, "RedisSecurityGroup", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = "Redis access from ECS",
            AllowAllOutbound = true
        });

        redisSecurityGroup.AddIngressRule(props.EcsSecurityGroup, Port.Tcp(6379), "ECS to Redis");

        var subnetGroup = new CfnSubnetGroup(this, "RedisSubnetGroup", new CfnSubnetGroupProps
        {
            CacheSubnetGroupName = "logs2obs-redis-subnets",
            Description = "Private subnets for logs2obs Redis",
            SubnetIds = props.Vpc.PrivateSubnets.Select(subnet => subnet.SubnetId).ToArray()
        });

        var replicationGroup = new CfnReplicationGroup(this, "RedisReplicationGroup", new CfnReplicationGroupProps
        {
            ReplicationGroupId = "logs2obs-redis",
            ReplicationGroupDescription = "logs2obs Redis cluster",
            Engine = "redis",
            EngineVersion = "7.0",
            CacheNodeType = "cache.t4g.micro",
            NumCacheClusters = 1,
            AutomaticFailoverEnabled = false,
            CacheSubnetGroupName = subnetGroup.Ref,
            SecurityGroupIds = new[] { redisSecurityGroup.SecurityGroupId }
        });

        RedisEndpoint = replicationGroup.AttrPrimaryEndPointAddress;
        RedisPort = replicationGroup.AttrPrimaryEndPointPort;
    }
}
