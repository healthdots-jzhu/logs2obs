using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.WAFv2;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class NetworkStack : Stack
{
    public IVpc Vpc { get; }
    public ApplicationLoadBalancer Alb { get; }
    public ApplicationListener HttpsListener { get; }
    public SecurityGroup EcsSecurityGroup { get; }

    public NetworkStack(Construct scope, string id, StackProps props)
        : base(scope, id, props)
    {
        var azs = Fn.GetAzs();

        var vpcResource = new CfnVPC(this, "Vpc", new CfnVPCProps
        {
            CidrBlock = "10.0.0.0/16",
            EnableDnsSupport = true,
            EnableDnsHostnames = true,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-vpc" } }
        });

        var igw = new CfnInternetGateway(this, "InternetGateway", new CfnInternetGatewayProps
        {
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-igw" } }
        });

        var igwAttachment = new CfnVPCGatewayAttachment(this, "InternetGatewayAttachment", new CfnVPCGatewayAttachmentProps
        {
            VpcId = vpcResource.Ref,
            InternetGatewayId = igw.Ref
        });

        var publicSubnetAz1 = new CfnSubnet(this, "PublicSubnetAz1", new CfnSubnetProps
        {
            VpcId = vpcResource.Ref,
            CidrBlock = "10.0.0.0/24",
            AvailabilityZone = Fn.Select(0, azs),
            MapPublicIpOnLaunch = true,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-public-az1" } }
        });

        var publicSubnetAz2 = new CfnSubnet(this, "PublicSubnetAz2", new CfnSubnetProps
        {
            VpcId = vpcResource.Ref,
            CidrBlock = "10.0.1.0/24",
            AvailabilityZone = Fn.Select(1, azs),
            MapPublicIpOnLaunch = true,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-public-az2" } }
        });

        var privateSubnetAz1 = new CfnSubnet(this, "PrivateSubnetAz1", new CfnSubnetProps
        {
            VpcId = vpcResource.Ref,
            CidrBlock = "10.0.10.0/24",
            AvailabilityZone = Fn.Select(0, azs),
            MapPublicIpOnLaunch = false,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-private-az1" } }
        });

        var privateSubnetAz2 = new CfnSubnet(this, "PrivateSubnetAz2", new CfnSubnetProps
        {
            VpcId = vpcResource.Ref,
            CidrBlock = "10.0.11.0/24",
            AvailabilityZone = Fn.Select(1, azs),
            MapPublicIpOnLaunch = false,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-private-az2" } }
        });

        var publicRouteTable = new CfnRouteTable(this, "PublicRouteTable", new CfnRouteTableProps
        {
            VpcId = vpcResource.Ref,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-public-rt" } }
        });

        var publicDefaultRoute = new CfnRoute(this, "PublicDefaultRoute", new CfnRouteProps
        {
            RouteTableId = publicRouteTable.Ref,
            DestinationCidrBlock = "0.0.0.0/0",
            GatewayId = igw.Ref
        });

        publicDefaultRoute.AddDependency(igwAttachment);

        _ = new CfnSubnetRouteTableAssociation(this, "PublicSubnetAz1Assoc", new CfnSubnetRouteTableAssociationProps
        {
            RouteTableId = publicRouteTable.Ref,
            SubnetId = publicSubnetAz1.Ref
        });

        _ = new CfnSubnetRouteTableAssociation(this, "PublicSubnetAz2Assoc", new CfnSubnetRouteTableAssociationProps
        {
            RouteTableId = publicRouteTable.Ref,
            SubnetId = publicSubnetAz2.Ref
        });

        var eip = new CfnEIP(this, "NatEip", new CfnEIPProps
        {
            Domain = "vpc"
        });

        var natGateway = new CfnNatGateway(this, "NatGateway", new CfnNatGatewayProps
        {
            SubnetId = publicSubnetAz1.Ref,
            AllocationId = eip.AttrAllocationId,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-nat" } }
        });

        natGateway.AddDependency(igwAttachment);

        var privateRouteTable = new CfnRouteTable(this, "PrivateRouteTable", new CfnRouteTableProps
        {
            VpcId = vpcResource.Ref,
            Tags = new[] { new CfnTag { Key = "Name", Value = "logs2obs-private-rt" } }
        });

        _ = new CfnRoute(this, "PrivateDefaultRoute", new CfnRouteProps
        {
            RouteTableId = privateRouteTable.Ref,
            DestinationCidrBlock = "0.0.0.0/0",
            NatGatewayId = natGateway.Ref
        });

        _ = new CfnSubnetRouteTableAssociation(this, "PrivateSubnetAz1Assoc", new CfnSubnetRouteTableAssociationProps
        {
            RouteTableId = privateRouteTable.Ref,
            SubnetId = privateSubnetAz1.Ref
        });

        _ = new CfnSubnetRouteTableAssociation(this, "PrivateSubnetAz2Assoc", new CfnSubnetRouteTableAssociationProps
        {
            RouteTableId = privateRouteTable.Ref,
            SubnetId = privateSubnetAz2.Ref
        });

        var importedVpc = Amazon.CDK.AWS.EC2.Vpc.FromVpcAttributes(this, "VpcAttributes", new VpcAttributes
        {
            VpcId = vpcResource.Ref,
            AvailabilityZones = azs,
            PublicSubnetIds = new[] { publicSubnetAz1.Ref, publicSubnetAz2.Ref },
            PrivateSubnetIds = new[] { privateSubnetAz1.Ref, privateSubnetAz2.Ref },
            PublicSubnetRouteTableIds = new[] { publicRouteTable.Ref, publicRouteTable.Ref },
            PrivateSubnetRouteTableIds = new[] { privateRouteTable.Ref, privateRouteTable.Ref }
        });
        Vpc = importedVpc;

        var albSecurityGroup = new SecurityGroup(this, "AlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "ALB security group",
            AllowAllOutbound = true
        });

        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "Allow HTTPS");
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "Allow HTTP");

        Alb = new ApplicationLoadBalancer(this, "Alb", new ApplicationLoadBalancerProps
        {
            Vpc = Vpc,
            InternetFacing = true,
            SecurityGroup = albSecurityGroup,
            LoadBalancerName = "logs2obs-alb",
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC }
        });

        var domainName = Node.TryGetContext("domainName")?.ToString() ?? "logs2obs.example.com";
        var certificate = new Certificate(this, "AlbCertificate", new CertificateProps
        {
            DomainName = domainName,
            Validation = CertificateValidation.FromDns()
        });

        HttpsListener = Alb.AddListener("HttpsListener", new BaseApplicationListenerProps
        {
            Port = 443,
            Protocol = ApplicationProtocol.HTTPS,
            Certificates = new IListenerCertificate[] { ListenerCertificate.FromCertificateManager(certificate) },
            Open = true
        });

        _ = Alb.AddListener("HttpListener", new BaseApplicationListenerProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP,
            Open = true,
            DefaultAction = ListenerAction.Redirect(new RedirectOptions
            {
                Protocol = "HTTPS",
                Port = "443",
                Permanent = true
            })
        });

        var webAcl = new CfnWebACL(this, "WebAcl", new CfnWebACLProps
        {
            Name = "logs2obs-waf",
            Scope = "REGIONAL",
            DefaultAction = new CfnWebACL.DefaultActionProperty
            {
                Allow = new CfnWebACL.AllowActionProperty()
            },
            VisibilityConfig = new CfnWebACL.VisibilityConfigProperty
            {
                CloudWatchMetricsEnabled = true,
                MetricName = "logs2obs-waf",
                SampledRequestsEnabled = true
            },
            Rules = new[]
            {
                CreateManagedRule("AWSManagedRulesCommonRuleSet", 0),
                CreateManagedRule("AWSManagedRulesKnownBadInputsRuleSet", 1)
            }
        });

        _ = new CfnWebACLAssociation(this, "WebAclAssociation", new CfnWebACLAssociationProps
        {
            ResourceArn = Alb.LoadBalancerArn,
            WebAclArn = webAcl.AttrArn
        });

        EcsSecurityGroup = new SecurityGroup(this, "EcsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "ECS security group",
            AllowAllOutbound = true
        });

        EcsSecurityGroup.AddIngressRule(albSecurityGroup, Port.Tcp(8080), "Allow ALB to ECS");
    }

    private static CfnWebACL.RuleProperty CreateManagedRule(string name, int priority)
    {
        return new CfnWebACL.RuleProperty
        {
            Name = name,
            Priority = priority,
            Statement = new CfnWebACL.StatementProperty
            {
                ManagedRuleGroupStatement = new CfnWebACL.ManagedRuleGroupStatementProperty
                {
                    Name = name,
                    VendorName = "AWS"
                }
            },
            OverrideAction = new CfnWebACL.OverrideActionProperty
            {
                None = new Dictionary<string, object>()
            },
            VisibilityConfig = new CfnWebACL.VisibilityConfigProperty
            {
                CloudWatchMetricsEnabled = true,
                MetricName = name,
                SampledRequestsEnabled = true
            }
        };
    }
}
