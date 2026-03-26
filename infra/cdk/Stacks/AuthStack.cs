using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Constructs;

namespace Logs2Obs.Cdk.Stacks;

public class AuthStackProps : StackProps
{
    public ITable TenantsTable { get; set; } = null!;
}

public class AuthStack : Stack
{
    public UserPool UserPool { get; }
    public UserPoolClient UserPoolClient { get; }

    public AuthStack(Construct scope, string id, AuthStackProps props)
        : base(scope, id, props)
    {
        UserPool = new UserPool(this, "UserPool", new UserPoolProps
        {
            UserPoolName = "logs2obs-users",
            SelfSignUpEnabled = false,
            Mfa = Mfa.OPTIONAL,
            MfaSecondFactor = new MfaSecondFactor
            {
                Otp = true,
                Sms = false
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 12,
                RequireDigits = true,
                RequireLowercase = true,
                RequireUppercase = true,
                RequireSymbols = true
            },
            SignInAliases = new SignInAliases
            {
                Email = true
            },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true, Mutable = true },
                GivenName = new StandardAttribute { Required = false, Mutable = true }
            }
        });

        var preTokenGeneration = new Function(this, "PreTokenGeneration", new FunctionProps
        {
            Runtime = Runtime.NODEJS_20_X,
            Handler = "index.handler",
            Timeout = Duration.Seconds(10),
            Environment = new Dictionary<string, string>
            {
                ["TENANTS_TABLE"] = props.TenantsTable.TableName
            },
            Code = Code.FromInline(
                """
                const AWS = require("aws-sdk");
                const ddb = new AWS.DynamoDB.DocumentClient();

                exports.handler = async (event) => {
                  const tableName = process.env.TENANTS_TABLE;
                  let tenantId = null;

                  try {
                    const result = await ddb.get({
                      TableName: tableName,
                      Key: { tenantId: event.userName }
                    }).promise();
                    tenantId = result && result.Item ? result.Item.tenantId : null;
                  } catch (err) {
                    console.log("tenant lookup failed", err);
                  }

                  if (tenantId) {
                    event.response = event.response || {};
                    event.response.claimsOverrideDetails = {
                      claimsToAddOrOverride: { tenantId }
                    };
                  }

                  return event;
                };
                """)
        });

        props.TenantsTable.GrantReadData(preTokenGeneration);
        UserPool.AddTrigger(UserPoolOperation.PRE_TOKEN_GENERATION, preTokenGeneration);

        UserPoolClient = new UserPoolClient(this, "UserPoolClient", new UserPoolClientProps
        {
            UserPool = UserPool,
            UserPoolClientName = "logs2obs-api-client",
            GenerateSecret = false,
            AuthFlows = new AuthFlow
            {
                UserPassword = true
            },
            AccessTokenValidity = Duration.Hours(1),
            IdTokenValidity = Duration.Hours(1),
            RefreshTokenValidity = Duration.Days(30)
        });
    }
}
