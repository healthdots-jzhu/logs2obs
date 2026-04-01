# Security Guide

## Overview

logs2obs implements defense-in-depth security with multi-layered isolation, dual authentication, SQL injection prevention, rate limiting, and comprehensive audit logging. This guide covers all security mechanisms for production deployments.

---

## 1. Dual Authentication Setup

logs2obs supports two authentication methods: **API Keys** (for service-to-service) and **JWT** (for human users and dashboards).

### API Key Authentication

#### Creating an API Key

Use this endpoint to generate a new API key (requires JWT authentication):

```bash
curl -X POST http://localhost:8080/api/v1/auth/keys \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "description": "CI/CD pipeline key",
    "expiresInDays": 365
  }'
```

Response:
```json
{
  "keyId": "key_01HZ9X...",
  "apiKey": "ls_live_abc123def456...",
  "tenantId": "tenant_01HZ...",
  "description": "CI/CD pipeline key",
  "createdAt": "2026-03-24T10:00:00Z",
  "expiresAt": "2027-03-24T10:00:00Z"
}
```

**⚠️ CRITICAL:** Store the `apiKey` value securely. It's only shown once and cannot be retrieved again.

#### Using an API Key

Include the API key in the `X-Api-Key` header:

```bash
curl -X POST http://localhost:8080/api/v1/logs \
  -H "X-Api-Key: ls_live_abc123def456..." \
  -H "Content-Type: application/json" \
  -d '{"entries": [...]}'
```

#### Rotating an API Key

1. Create a new key
2. Update your services to use the new key
3. Delete the old key:

```bash
curl -X DELETE http://localhost:8080/api/v1/auth/keys/key_01HZ9X... \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

#### When to Use API Keys

- **Service-to-service communication** (log ingestion from apps)
- **Automated scripts** (cron jobs, CI/CD pipelines)
- **Long-lived integrations** (alerting systems, dashboards with backend)

---

### JWT Authentication (Multi-IdP)

logs2obs supports **flexible, multi-identity provider (IdP) JWT authentication** via OIDC discovery. No client secrets required — public keys are fetched automatically. Configure one or more OIDC-compliant IdPs in `appsettings.json` under `Auth:IdentityProviders`.

#### Multi-IdP Configuration

Each identity provider entry specifies:
- **`Name`** — Scheme name (e.g., "Cognito-Prod", "Entra-Dev")
- **`Authority`** — OIDC authority URL; ASP.NET Core auto-appends `/.well-known/openid-configuration`
- **`Audiences`** — Array of accepted client IDs; validation skipped if empty
- **`ClaimsMappings`** — Map IdP-specific claim names to canonical `tenantId` claim

Example `appsettings.json`:

```json
{
  "Auth": {
    "IdentityProviders": [
      {
        "Name": "Cognito-Default",
        "Authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_abc123XYZ",
        "Audiences": [ "1a2b3c4d5e6f7g8h9i0j", "2b3c4d5e6f7g8h9i0j1k" ],
        "ClaimsMappings": {
          "custom:tenantId": "tenantId"
        }
      },
      {
        "Name": "EntraID",
        "Authority": "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0",
        "Audiences": [ "api://logs2obs" ],
        "ClaimsMappings": {
          "extension_tenantId": "tenantId"
        }
      }
    ]
  }
}
```

**OIDC Discovery (automatic):**
1. ASP.NET Core fetches `{Authority}/.well-known/openid-configuration`
2. Extracts the JWKS endpoint (`jwks_uri`)
3. Fetches public signing keys; RS256 asymmetric validation — no secrets stored
4. Token validation cached; keys refreshed on unknown `kid` (key ID)

**Claims Normalization:**
The `ClaimsNormalizationMiddleware` maps IdP-specific claim names to canonical names:
- **Cognito:** `custom:tenantId` → `tenantId`
- **Entra ID:** `extension_tenantId` → `tenantId`
- **Okta/Generic:** Custom claim names as needed

#### Cognito-Specific Example

To set up AWS Cognito with logs2obs, deploy the `AuthStack` in your CDK infrastructure:

```csharp
// infra/cdk/Stacks/AuthStack.cs (excerpt)
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
    }
});

// Pre-Token Generation Lambda injects tenantId claim
UserPool.AddTrigger(UserPoolOperation.PRE_TOKEN_GENERATION, preTokenGeneration);
```

Obtain a JWT token via AWS Cognito:

```bash
# 1. Initiate authentication
aws cognito-idp initiate-auth \
  --auth-flow USER_PASSWORD_AUTH \
  --client-id YOUR_CLIENT_ID \
  --auth-parameters USERNAME=user@example.com,PASSWORD='SecurePass123!' \
  --region us-east-1

# 2. If MFA enabled, respond to challenge
aws cognito-idp respond-to-auth-challenge \
  --challenge-name SOFTWARE_TOKEN_MFA \
  --client-id YOUR_CLIENT_ID \
  --session "SESSION_TOKEN_FROM_STEP_1" \
  --challenge-responses USERNAME=user@example.com,SOFTWARE_TOKEN_MFA_CODE=123456
```

The response contains `IdToken` (use this for API authentication):

```json
{
  "AuthenticationResult": {
    "IdToken": "eyJraWQiOiJ...",
    "AccessToken": "eyJraWQiOiJ...",
    "RefreshToken": "eyJjdHkiOiJ...",
    "ExpiresIn": 3600
  }
}
```

#### Entra ID (Azure AD) Example

Configure Entra ID as a trusted IdP:

```json
{
  "Auth": {
    "IdentityProviders": [
      {
        "Name": "EntraID",
        "Authority": "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0",
        "Audiences": [ "api://logs2obs" ],
        "ClaimsMappings": {
          "extension_tenantId": "tenantId"
        }
      }
    ]
  }
}
```

Obtain a token using the Microsoft identity library or OAuth2 authorization code flow:

```bash
curl -X POST https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/oauth2/v2.0/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "client_id=YOUR_CLIENT_ID&scope=api://logs2obs/.default&grant_type=client_credentials&client_secret=YOUR_CLIENT_SECRET"
```

#### Okta / Generic OIDC Example

Any OIDC-compliant provider (Okta, Auth0, generic OpenID Connect servers) works:

```json
{
  "Auth": {
    "IdentityProviders": [
      {
        "Name": "Okta",
        "Authority": "https://my-org.okta.com",
        "Audiences": [ "0oa1a2b3c4d5e6f7g8h9" ],
        "ClaimsMappings": {
          "org.logs2obs.tenant": "tenantId"
        }
      }
    ]
  }
}
```

#### Multiple Cognito Pools Example

Use `ClaimsMappings` to unify claims across multiple Cognito environments:

```json
{
  "Auth": {
    "IdentityProviders": [
      {
        "Name": "Cognito-Dev",
        "Authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_devPool",
        "Audiences": [ "dev-client-id" ],
        "ClaimsMappings": {
          "custom:tenantId": "tenantId"
        }
      },
      {
        "Name": "Cognito-Prod",
        "Authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_prodPool",
        "Audiences": [ "prod-client-id" ],
        "ClaimsMappings": {
          "custom:tenantId": "tenantId"
        }
      }
    ]
  }
}
```

#### Using a JWT Token

Include the JWT in the `Authorization` header:

```bash
curl -X POST http://localhost:8080/api/v1/query/natural \
  -H "Authorization: Bearer eyJraWQiOiJ..." \
  -H "Content-Type: application/json" \
  -d '{"question": "How many errors in the last hour?"}'
```

#### When to Use JWT

- **Human users** (engineers using the web UI or CLI)
- **Single-page applications** (React/Vue dashboards)
- **Administrative operations** (creating API keys, managing pull jobs)

---

### Internal Service Authentication (M2M — Machine-to-Machine)

For service-to-service communication within your infrastructure, use Cognito **client credentials flow** (OAuth2 `client_credentials` grant). This follows Zero Trust principles — services authenticate with their own credentials, never using user tokens.

#### Cognito Client Credentials Setup

Configure a Cognito resource server and service client in CDK:

```csharp
// In AuthStack.cs
var resourceServer = UserPool.AddResourceServer("ResourceServer", new UserPoolResourceServerOptions
{
    Identifier = "logs2obs-api",
    Scopes = new[] {
        new ResourceServerScope { ScopeName = "ingest", ScopeDescription = "Ingest logs" },
        new ResourceServerScope { ScopeName = "query", ScopeDescription = "Query logs" }
    }
});

// Create a machine-to-machine client
var m2mClient = UserPool.AddClient("M2MClient", new UserPoolClientOptions
{
    ClientName = "logs2obs-ingest-worker",
    AuthFlows = new AuthFlow { ClientCredentials = true },
    OAuthScopes = new[] { OAuthScope.ResourceServer(resourceServer, new ResourceServerScope { ScopeName = "ingest" }) }
});
```

#### Obtaining an M2M Token

```bash
curl -X POST https://logs2obs.auth.us-east-1.amazoncognito.com/oauth2/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=YOUR_M2M_CLIENT_ID&client_secret=YOUR_M2M_CLIENT_SECRET&scope=logs2obs-api/ingest"
```

Response:

```json
{
  "access_token": "eyJraWQiOiJ...",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

#### Using M2M Token for Service-to-Service Calls

```bash
curl -X POST http://api:8080/api/v1/logs \
  -H "Authorization: Bearer eyJraWQiOiJ..." \
  -H "Content-Type: application/json" \
  -d '{"entries": [...]}'
```

**Scope verification:** logs2obs validates the `scope` claim in the token; ingest workers must hold `logs2obs-api/ingest` or have API key fallback.

---

### Endpoint Authentication Matrix

| Endpoint | API Key | JWT | Notes |
|----------|---------|-----|-------|
| `POST /api/v1/logs` | ✅ | ✅ | Log ingestion |
| `POST /api/v1/query/sql` | ✅ | ✅ | SQL query execution |
| `POST /api/v1/query/natural` | ❌ | ✅ | AI queries require JWT |
| `POST /api/v1/auth/keys` | ❌ | ✅ | Only JWT can create API keys |
| `DELETE /api/v1/auth/keys/{id}` | ❌ | ✅ | Only JWT can delete API keys |
| `POST /api/v1/pull-jobs` | ❌ | ✅ | Administrative operation |
| `POST /api/v1/graphs/render` | ✅ | ✅ | Both allowed |
| `GET /health/ready` | ⚪ | ⚪ | No auth required |

---

## 2. Tenant Isolation

logs2obs enforces **hard multi-tenancy** with isolation at every layer of the stack. A tenant can **never** access another tenant's data.

### API Layer: TenantId Injection

The `TenantContextMiddleware` extracts `tenantId` from the authenticated context and injects it into `HttpContext.Items`:

```csharp
// API key authentication
var tenantIdClaim = context.User.FindFirst("tenantId");
context.Items["TenantId"] = tenantIdClaim.Value;

// JWT authentication
// Pre-Token Generation Lambda injects tenantId claim from DynamoDB lookup
var tenantIdClaim = jwtToken.Claims.First(c => c.Type == "tenantId");
context.Items["TenantId"] = tenantIdClaim.Value;
```

**Critical Rule:** The `tenantId` is **never** accepted from request body or query string. It always comes from the authentication token.

---

### SQL Layer: Automatic Query Injection

The `TenantQueryInjector` appends `WHERE tenant_id = '...'` to every query:

```csharp
// Original query
var sql = "SELECT * FROM logs WHERE level = 'error' LIMIT 100";

// Rewritten query (automatic)
var sql = "SELECT * FROM logs WHERE tenant_id = 'tenant_01HZ...' AND level = 'error' LIMIT 100";
```

**Bypass Prevention:** The injector runs **after** user-provided SQL is parsed but **before** execution, ensuring no escaping or comment tricks can bypass it.

---

### Parquet Storage: S3 Prefix Isolation

All Parquet files are written to tenant-specific prefixes:

```
s3://logs2obs-logs/
  tenant_01HZ.../
    year=2026/
      month=03/
        day=24/
          batch_abc123.parquet
          batch_def456.parquet
```

**IAM Policy Example:**
```json
{
  "Effect": "Allow",
  "Action": ["s3:GetObject", "s3:PutObject"],
  "Resource": "arn:aws:s3:::logs2obs-logs/${tenantId}/*"
}
```

---

### OpenSearch: Document-Level Filtering

Every document indexed in OpenSearch includes a `tenantId` field:

```json
{
  "_index": "logs2obs-logs-2026.03.24",
  "_id": "01HZABC...",
  "_source": {
    "tenantId": "tenant_01HZ...",
    "timestamp": "2026-03-24T10:00:00Z",
    "level": "error",
    "message": "NullRef in PaymentProcessor"
  }
}
```

All queries are filtered:
```json
{
  "query": {
    "bool": {
      "must": [
        { "term": { "tenantId": "tenant_01HZ..." } },
        { "match": { "level": "error" } }
      ]
    }
  }
}
```

**Index Alias Strategy:** Tenants share the same physical index but are logically isolated by document-level filtering.

---

### DynamoDB: Partition Key Isolation

All metadata tables use `tenantId` as the partition key:

```
Table: tenants
  PK: tenantId (e.g., "tenant_01HZ...")
  Attributes: name, createdAt, rateLimitConfig

Table: saved-queries
  PK: tenantId
  SK: queryId
  Attributes: sql, name, description

Table: pull-jobs
  PK: tenantId
  SK: jobId
  Attributes: sourceType, schedule, config
```

**Access Pattern:** All DynamoDB queries **must** include `tenantId` in the key condition:

```csharp
var response = await _dynamoClient.QueryAsync(new QueryRequest
{
    TableName = "saved-queries",
    KeyConditionExpression = "tenantId = :tid",
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":tid"] = new AttributeValue { S = tenantId }
    }
});
```

---

### Redis: Key Prefixing

All Redis keys include the `tenantId` prefix:

```
{tenant_01HZ...}:idempotency:01HZABC...
{tenant_01HZ...}:matview:error-rate-heatmap
{tenant_01HZ...}:apikey:ls_live_abc123...
```

**Isolation Guarantee:** Redis key namespacing ensures tenant data never collides, even in shared Redis cluster.

---

## 3. SQL Safety Rules

The `SqlSafetyValidator` enforces strict rules on all SQL queries (including AI-generated queries) to prevent data corruption and unauthorized access.

### Forbidden Keywords

These SQL keywords are **always rejected**:

| Keyword | Reason |
|---------|--------|
| `DROP` | Prevents table/schema deletion |
| `DELETE` | Prevents data deletion (logs are append-only) |
| `INSERT` | Prevents data injection |
| `CREATE` | Prevents schema modification |
| `ALTER` | Prevents schema modification |
| `TRUNCATE` | Prevents data loss |
| `UPDATE` | Prevents data modification |
| `GRANT` | Prevents privilege escalation |
| `REVOKE` | Prevents privilege modification |

**Error Response:**
```json
{
  "error": "ForbiddenSqlKeyword",
  "message": "SQL contains forbidden keyword: DROP",
  "code": 400
}
```

---

### Warned Keywords

These keywords are **allowed but flagged** for review:

| Keyword | Warning |
|---------|---------|
| `CROSS JOIN` | Can cause Cartesian explosion; ensure intentional |

**Response (with warning):**
```json
{
  "queryId": "qry_01HZ...",
  "warnings": [
    "Query contains CROSS JOIN which may scan large datasets. Ensure this is intentional."
  ]
}
```

---

### Required Clauses

Every query **must** include:

1. **Partition filter** (year/month/day)
2. **LIMIT clause** (max: 10,000 rows)

**Invalid Query:**
```sql
SELECT * FROM logs WHERE level = 'error'
```

**Error Response:**
```json
{
  "error": "MissingPartitionFilter",
  "message": "Query must include partition filters (year, month, day). Example: AND year='2026' AND month='03' AND day='24'",
  "code": 400
}
```

**Valid Query:**
```sql
SELECT * FROM logs
WHERE level = 'error'
  AND year = '2026' AND month = '03' AND day = '24'
LIMIT 100
```

---

### AI-Generated SQL Safety

Every AI-generated query is:

1. **Validated** by `SqlSafetyValidator` before execution
2. **Logged** in the `query-executions` audit table with `generatedByAi: true`
3. **Attributed** to the user's `tenantId` and `userId`

**Audit Log Entry:**
```json
{
  "queryId": "qry_01HZ...",
  "tenantId": "tenant_01HZ...",
  "userId": "user_01HZ...",
  "generatedByAi": true,
  "naturalLanguageQuestion": "How many fatal errors per service yesterday?",
  "generatedSql": "SELECT sourceid, COUNT(*) FROM logs WHERE level='fatal' AND year='2026' AND month='03' AND day='23' GROUP BY sourceid LIMIT 100",
  "validationResult": "passed",
  "executedAt": "2026-03-24T10:00:00Z",
  "executionTimeMs": 234,
  "costUsd": 0.0012
}
```

---

## 4. Rate Limiting

logs2obs implements **token bucket rate limiting** on a per-tenant basis to prevent abuse and ensure fair resource allocation.

### Default Limits

| Tenant Tier | Requests/Minute | Burst Capacity |
|-------------|-----------------|----------------|
| Free | 60 | 10 |
| Pro | 600 | 50 |
| Enterprise | 6,000 | 200 |

### Rate Limit Response

When rate limit is exceeded:

```json
{
  "error": "RateLimitExceeded",
  "message": "Rate limit exceeded for tenant. Try again in 34 seconds.",
  "retryAfterSeconds": 34,
  "code": 429
}
```

**HTTP Headers:**
```
HTTP/1.1 429 Too Many Requests
X-RateLimit-Limit: 600
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1711279234
Retry-After: 34
```

---

### Increasing Per-Tenant Limits

Update the tenant record in DynamoDB:

```bash
aws dynamodb update-item \
  --table-name logs2obs-tenants \
  --key '{"tenantId": {"S": "tenant_01HZ..."}}' \
  --update-expression "SET rateLimitConfig.requestsPerMinute = :rpm, rateLimitConfig.burstCapacity = :burst" \
  --expression-attribute-values '{
    ":rpm": {"N": "1200"},
    ":burst": {"N": "100"}
  }'
```

**Cache TTL:** Changes take effect within 60 seconds (rate limit config cache TTL).

---

## 5. Secret Management

logs2obs supports multiple secret stores depending on the deployment environment.

### Local Development (appsettings.json)

```json
{
  "Logs2Obs": {
    "Provider": "Local",
    "Secrets": {
      "OpenSearchPassword": "admin",
      "RedisPassword": "",
      "DatabasePassword": "postgres"
    }
  }
}
```

**⚠️ Never commit secrets to version control.** Use .NET User Secrets for local dev:

```bash
dotnet user-secrets set "Logs2Obs:Secrets:OpenSearchPassword" "admin"
```

---

### AWS (Secrets Manager)

Set `Logs2Obs__Provider=AWS` and store secrets in AWS Secrets Manager:

```bash
aws secretsmanager create-secret \
  --name logs2obs/opensearch \
  --secret-string '{"username":"admin","password":"SuperSecure123!"}'

aws secretsmanager create-secret \
  --name logs2obs/redis \
  --secret-string '{"password":"RedisSecure456!"}'
```

**Configuration:**
```json
{
  "Logs2Obs": {
    "Provider": "AWS",
    "AWS": {
      "SecretsManagerPrefix": "logs2obs/"
    }
  }
}
```

**IAM Policy Required:**
```json
{
  "Effect": "Allow",
  "Action": [
    "secretsmanager:GetSecretValue"
  ],
  "Resource": "arn:aws:secretsmanager:us-east-1:123456789012:secret:logs2obs/*"
}
```

---

### Azure (Key Vault)

Set `Logs2Obs__Provider=Azure` and store secrets in Azure Key Vault:

```bash
az keyvault secret set \
  --vault-name logs2obs-keyvault \
  --name opensearch-password \
  --value "SuperSecure123!"
```

---

### GCP (Secret Manager)

Set `Logs2Obs__Provider=GCP` and store secrets in GCP Secret Manager:

```bash
echo -n "SuperSecure123!" | gcloud secrets create opensearch-password --data-file=-
```

---

### GitHub Actions Secrets

Three secrets must be configured in **GitHub → Settings → Secrets and variables → Actions**:

| Secret | Format | Example | How to find it |
|---|---|---|---|
| `AWS_REGION` | AWS region string | `us-east-1` | Your target deployment region |
| `AWS_ROLE_TO_ASSUME` | Full IAM role ARN | `arn:aws:iam::123456789012:role/logs2obs-github-deploy` | AWS Console → IAM → Roles → your role → ARN |
| `ECR_REGISTRY` | ECR registry hostname | `123456789012.dkr.ecr.us-east-1.amazonaws.com` | AWS Console → ECR → any repo URI, strip the repo name suffix |

#### Setting up the OIDC trust policy

`AWS_ROLE_TO_ASSUME` uses OIDC (no long-lived keys). The IAM role needs:

**Trust policy:**
```json
{
  "Effect": "Allow",
  "Principal": {
    "Federated": "arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
  },
  "Action": "sts:AssumeRoleWithWebIdentity",
  "Condition": {
    "StringLike": {
      "token.actions.githubusercontent.com:sub": "repo:YOUR_ORG/logs2obs:*"
    }
  }
}
```

**Required permissions:**
- `ecr:GetAuthorizationToken`, `ecr:BatchCheckLayerAvailability`, `ecr:PutImage`, `ecr:InitiateLayerUpload`, `ecr:UploadLayerPart`, `ecr:CompleteLayerUpload`
- `ecs:UpdateService`, `ecs:DescribeServices`

The CDK `AuthStack` does not create this role automatically — create it manually in IAM or add it to the CDK `ComputeStack`.

---

## 6. Network Topology (AWS)

### VPC Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        VPC 10.0.0.0/16                       │
│                                                              │
│  ┌───────────────────────────┐  ┌──────────────────────┐   │
│  │  PUBLIC SUBNETS           │  │  PRIVATE SUBNETS      │   │
│  │  10.0.0.0/24 (AZ1)        │  │  10.0.10.0/24 (AZ1)   │   │
│  │  10.0.1.0/24 (AZ2)        │  │  10.0.11.0/24 (AZ2)   │   │
│  │                           │  │                       │   │
│  │  ┌─────────────────────┐  │  │  ┌─────────────────┐ │   │
│  │  │  ALB                │  │  │  │  ECS Fargate    │ │   │
│  │  │  (Internet-facing)  │  │  │  │  (API, Worker)  │ │   │
│  │  └─────────┬───────────┘  │  │  └────────┬────────┘ │   │
│  │            │               │  │           │          │   │
│  │  ┌─────────▼───────────┐  │  │  ┌────────▼────────┐ │   │
│  │  │  WAF                │  │  │  │  OpenSearch     │ │   │
│  │  │  - Rate limiting    │  │  │  │  ElastiCache    │ │   │
│  │  │  - IP allowlist     │  │  │  │  (No internet)  │ │   │
│  │  │  - AWS Managed Rules│  │  │  └─────────────────┘ │   │
│  │  └─────────────────────┘  │  │                       │   │
│  │                           │  │  ┌─────────────────┐ │   │
│  │  ┌─────────────────────┐  │  │  │  NAT Gateway    │ │   │
│  │  │  Internet Gateway   │◄─┼──┼──┤  (Egress only)  │ │   │
│  │  └─────────────────────┘  │  │  └─────────────────┘ │   │
│  └───────────────────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### Security Group Rules

**ALB Security Group:**
- Inbound: 443 (HTTPS) from 0.0.0.0/0
- Inbound: 80 (HTTP) from 0.0.0.0/0 (redirects to 443)
- Outbound: 8080 (to ECS security group)

**ECS Security Group:**
- Inbound: 8080 (from ALB security group only)
- Outbound: 443 (HTTPS to S3, DynamoDB, OpenSearch)
- Outbound: 6379 (to ElastiCache security group)

**No Direct Internet Access:** ECS tasks use NAT Gateway for outbound connections (AWS API calls, package downloads).

---

### WAF Rules

The `NetworkStack` attaches a Web ACL with these rule groups and custom rules:

```csharp
// Priority 0: AWS Common Rule Set
CreateManagedRule("AWSManagedRulesCommonRuleSet", 0),

// Priority 1: Known Bad Inputs Rule Set
CreateManagedRule("AWSManagedRulesKnownBadInputsRuleSet", 1),

// Priority 2: Rate-based rule (see Infrastructure Security section)
// Priority 3: IP Reputation List (see Infrastructure Security section)
```

---

## 6a. Infrastructure Security (AWS WAF)

### ForwardedHeaders Middleware (X-Forwarded-For / X-Forwarded-Proto)

When logs2obs is deployed behind an AWS Application Load Balancer (ALB), the ALB proxies traffic to ECS Fargate tasks. Without proper header forwarding, the API sees all requests as coming from the ALB's internal IP instead of the actual client IP.

**Configuration (ASP.NET Core Program.cs):**

```csharp
// UseForwardedHeaders MUST be the FIRST middleware in the pipeline
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Trust only RFC 1918 private ranges (ALB's subnet)
    AllowedHosts = null,  // Allow all hosts; filtering done by IP ranges
    TrustedProxies = null,  // We use networks instead
    TrustedNetworks = new()
    {
        System.Net.IPNetwork.Parse("10.0.0.0/8"),        // Class A private
        System.Net.IPNetwork.Parse("172.16.0.0/12"),     // Class B private
        System.Net.IPNetwork.Parse("192.168.0.0/16")     // Class C private
    }
};

app.UseForwardedHeaders(forwardedHeadersOptions);
// ... rest of middleware pipeline
```

**Why This Matters:**

Without `UseForwardedHeaders()`, the rate limiter buckets all anonymous traffic under the ALB's internal IP (e.g., 10.0.10.5). This defeats per-client-IP rate limiting — a single aggressive client can effectively exhaust the entire tenant's rate limit quota by appearing as multiple "tenants" through the same IP.

**Result with proper configuration:**
- Client 192.168.1.100 makes 100 requests → rate limiter sees 100 from 192.168.1.100
- Client 192.168.1.101 makes 50 requests → rate limiter sees 50 from 192.168.1.101
- Both stay within per-client limits even if the ALB processes them

**Result without `UseForwardedHeaders()` (INCORRECT):**
- Client 192.168.1.100 + 192.168.1.101 both appear as ALB IP 10.0.10.5
- Rate limiter sees 150 requests from 10.0.10.5
- Single aggressive client can consume entire tenant quota

---

### WAF Rate-Based Rule (L7 DDoS Protection)

A custom WAF rate-based rule at **priority 2** blocks IP addresses that exceed a threshold:

```csharp
// infra/cdk/Stacks/NetworkStack.cs
new RateBasedStatementProperty
{
    Limit = 2000,  // requests
    AggregateKeyType = "IP",
    ScopeDownStatement = null  // Apply to all traffic
}
```

**Action:** `Block` — matching requests receive HTTP 403 Forbidden

**Window:** 5 minutes

**Why:** Mitigates Layer 7 (application-layer) DDoS attacks where an attacker floods the ALB with legitimate-looking HTTP requests. The rate-based rule catches patterns that individual WAF managed rules might miss.

**Behavior:**
- IP making 2,001+ requests in 5 minutes → blocked for remainder of window
- IP making <2,000 requests in 5 minutes → allowed
- Block status resets every 5 minutes

---

### AWS Managed IP Reputation List (Priority 3)

The WAF also applies AWS Managed Rules IP Reputation List at **priority 3**:

```csharp
new ManagedRuleGroupStatementProperty
{
    Name = "AWSManagedRulesAmazonIpReputationList",
    VendorName = "AWS",
}
```

**Data Source:** AWS threat intelligence feed — blocks IPs known to be:
- Hosting malware
- Operating botnets
- Recently compromised
- Performing credential stuffing
- Engaging in scanning/reconnaissance

**Action:** `Block` — requests from reputation-listed IPs are rejected before reaching the application

**Advantage over rate-based rule:** Catches bad actors on first request, before they accumulate rate-limit violations.

---

### WAF Logging

All WAF activity is logged to CloudWatch Logs for auditing and troubleshooting:

```csharp
// infra/cdk/Stacks/NetworkStack.cs
var wafLogGroup = new LogGroup(this, "WafLogGroup", new LogGroupProps
{
    LogGroupName = "aws-waf-logs-logs2obs",
    Retention = RetentionDays.NINETY_DAYS,
    RemovalPolicy = RemovalPolicy.RETAIN
});

webAcl.LoggingConfiguration = new LoggingConfigurationProperty
{
    ResourceArn = webAcl.AttrArn,
    LogDestinationConfigs = new[] { wafLogGroup.LogGroupArn }
};
```

**Log Entry Example:**
```json
{
  "timestamp": 1711279200,
  "formatversion": 1,
  "webaclid": "arn:aws:wafv2:us-east-1:123456789012:regional/webacl/logs2obs/a1b2c3d4",
  "terminatingruleid": "RateBasedRule",
  "terminatingruletype": "RATE_BASED",
  "action": "BLOCK",
  "httpsourcename": "CF",
  "httpsourceid": "example-distribution",
  "rulegrouplist": [],
  "httprequest": {
    "clientip": "203.0.113.42",
    "country": "US",
    "method": "POST",
    "uri": "/api/v1/logs",
    "args": "",
    "httpversion": "HTTP/2.0",
    "headers": [
      { "name": "Host", "value": "logs.example.com" }
    ]
  }
}
```

**Query blocked logs in CloudWatch:**
```bash
aws logs filter-log-events \
  --log-group-name aws-waf-logs-logs2obs \
  --filter-pattern '{ $.action = "BLOCK" }' \
  --start-time $(date -d '1 hour ago' +%s)000 \
  --query 'events[*].message' | jq
```

---

## 6b. Application-Layer Hardening

### HTTP Security Headers

Every HTTP response from logs2obs includes a standard set of security headers to prevent common browser-based attacks. These headers are applied via `SecurityHeadersMiddleware` and are sent with all responses, regardless of content type.

**Configuration (Program.cs):**

```csharp
// SecurityHeadersMiddleware applies to all HTTP responses
app.UseMiddleware<SecurityHeadersMiddleware>();
```

**Headers Set:**

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing (blocks .js files from being executed as HTML, etc.) |
| `X-Frame-Options` | `DENY` | Prevents clickjacking — logs2obs cannot be embedded in an `<iframe>` by any site |
| `Referrer-Policy` | `no-referrer` | Prevents referrer leakage to third-party services when users navigate away |
| `X-XSS-Protection` | `0` | Explicitly disables legacy IE XSS filter (modern browsers ignore this; CSP handles XSS) |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | Restricts access to sensitive browser features (GPS, microphone, camera) |

**Example Response:**

```
HTTP/1.1 200 OK
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
X-XSS-Protection: 0
Permissions-Policy: geolocation=(), microphone=(), camera=()
Content-Type: application/json
...
```

**Attack Scenarios Prevented:**

1. **MIME-Type Sniffing:** Attacker uploads `malware.js` with `Content-Type: text/plain`. Without `X-Content-Type-Options: nosniff`, old browsers execute it as JavaScript. **With the header:** browser respects the declared type.

2. **Clickjacking:** Attacker embeds logs2obs in a hidden iframe and tricks users into clicking invisible buttons. **`X-Frame-Options: DENY`** prevents embedding entirely.

3. **Referrer Leakage:** User navigates from logs2obs to external site; browser normally sends `Referer: https://logs.internal.com/api/v1/query?...`. **`Referrer-Policy: no-referrer`** prevents this.

**Content-Security-Policy (CSP) — Intentionally Excluded:**

CSP is **not** included in the default headers because logs2obs uses dynamically generated graphs (Vega-Lite, Chart.js) and OpenAPI/Swagger UI. A blanket CSP policy would require `'unsafe-inline'` or `'unsafe-eval'`, defeating the purpose. CSP becomes practical only after:
- Graph rendering is moved to a separate, isolated iframe
- UI framework and OpenAPI viewer are locked to specific CDN origins

**Future:** Add CSP headers on a per-route basis when UI architecture stabilizes.

---

### HSTS (HTTP Strict Transport Security)

HSTS forces browsers to always use HTTPS when connecting to logs2obs, preventing downgrade attacks (e.g., MITM forcing HTTP → HTTP redirect).

**Configuration (Program.cs):**

```csharp
// In AddLogs2ObsApi():
services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);  // 1 year
    options.IncludeSubDomains = true;
    options.Preload = false;
});

// In app middleware pipeline (non-development only):
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
```

**Header Sent (non-development only):**

```
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

**What it does:**

- **`max-age=31536000`** (1 year): Browser caches this policy for 365 days. All requests to logs2obs (including subdomains) must use HTTPS. Violating requests are rejected.
- **`includeSubDomains`**: Policy applies to subdomains (e.g., `api.logs.example.com`, `admin.logs.example.com`).
- **`Preload`** (set to `false`): Do NOT preload into the browser's HSTS preload list. Only enable after verifying domain stability in production.

**Why non-development only?**

On `localhost`, HSTS breaks local dev tooling:
- curl/Postman cannot bypass certificate validation over HTTPS
- Docker containers cannot route to `localhost` HSTS-enforced addresses
- HTTP health checks fail

**Defense-in-Depth with ALB:**

logs2obs implements HSTS at both the edge and application layers:

1. **ALB (edge):** HTTP requests redirected to HTTPS at the load balancer
2. **Application (app):** HSTS header tells browsers to never send HTTP requests, even if redirects are intercepted

If an attacker somehow compromises the ALB's redirect logic, the HSTS header still protects users whose browsers have cached the policy.

**Preload Consideration:**

Setting `Preload = true` adds logs2obs to the public HSTS preload list, which is baked into browsers (Chrome, Firefox, Safari). This prevents MITM even on the first visit. However, once a domain is on the preload list, removal can take months. Only enable after:
- Domain is confirmed stable in production for ≥3 months
- All subdomains support HTTPS
- You have tested the `removeSubDomains` flow if subdomains ever stop supporting HTTPS

---

### Middleware Pipeline Order

The order of middleware in the ASP.NET Core pipeline is critical. A single out-of-order middleware can bypass security controls.

**Correct Order (Program.cs):**

```csharp
// 1. Fix real client IPs behind ALB
app.UseForwardedHeaders();

// 2. HSTS policy (non-dev only) — must be early to catch redirects
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// 3. Security headers (all responses)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 4. Request/response logging
app.UseSerilogRequestLogging();

// 5. Global exception handler
app.UseExceptionHandler();

// 6. Request timeout enforcement
app.UseRequestTimeouts();

// 7. Payload size validation
app.UseMiddleware<PayloadSizeMiddleware>();

// 8. Authentication / Authorization
app.UseAuthentication();
app.UseAuthorization();

// 9. Normalize tenant claims
app.UseMiddleware<ClaimsNormalizationMiddleware>();

// 10. Extract tenant from claims/header
app.UseMiddleware<TenantContextMiddleware>();

// 11. Rate limiting (must be after tenant context)
app.UseRateLimiter();

// 12. Map endpoints
app.MapControllers();
```

**Reference Table:**

| Priority | Middleware | Purpose | Security Impact |
|----------|------------|---------|-----------------|
| 1 | `UseForwardedHeaders()` | Restore real client IPs from ALB headers | Required for accurate rate limiting, auditing, IP-based access control |
| 2 | `UseHsts()` | Strict Transport Security (non-dev) | Prevents HTTP downgrade attacks |
| 3 | `SecurityHeadersMiddleware` | Apply security headers (X-Frame-Options, etc.) | Browser-level protection from MIME sniffing, clickjacking, XSS |
| 4 | `UseSerilogRequestLogging()` | Structured request/response logging | Audit trail for compliance (SOC 2, HIPAA) |
| 5 | `UseExceptionHandler()` | Catch unhandled exceptions, return safe error | Prevents information leakage in stack traces |
| 6 | `UseRequestTimeouts()` | Enforce per-endpoint timeout policies | Prevents runaway queries from exhausting thread pool |
| 7 | `PayloadSizeMiddleware` | Validate request body size limits | DoS prevention — blocks multi-GB uploads |
| 8 | `UseAuthentication()` / `UseAuthorization()` | API key, JWT validation | Establishes authenticated identity |
| 9 | `ClaimsNormalizationMiddleware` | Map IdP-specific claims to canonical `tenantId` | Tenant isolation depends on canonical claim |
| 10 | `TenantContextMiddleware` | Extract tenant from claims/header into context | Downstream code accesses `HttpContext.Items["tenant"]` |
| 11 | `UseRateLimiter()` | Per-tenant, per-IP rate limiting | DDoS mitigation at application layer |

**Critical Order Violations:**

- **Authentication after rate limiting?** Unauthenticated traffic consumes rate limit quota (usually acceptable, prevents unauthenticated DDoS).
- **ForwardedHeaders after rate limiting?** All traffic appears to come from ALB IP; per-client rate limiting broken.
- **Exception handler before authentication?** Attacker errors expose internal URLs/stack traces.
- **Tenant middleware before authentication?** Claims not yet populated; tenant extraction fails.

---

### Kestrel Minimum Data Rate (Slow Loris Mitigation)

Kestrel (the ASP.NET Core HTTP server) enforces minimum data rate thresholds to prevent **slow loris** attacks where attackers hold connections open by sending data very slowly or not at all.

**Configuration (Program.cs or appsettings.json):**

```csharp
app.UseKestrel(options =>
{
    // Request body must arrive at minimum 100 bytes/sec
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10)
    );

    // Response body must be sent at minimum 100 bytes/sec
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10)
    );

    // Headers must be sent within 15 seconds
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);

    // Keep-alive connections timeout after 120 seconds
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
});
```

**What each parameter does:**

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `MinRequestBodyDataRate` | 100 bytes/sec | Rejects uploads slower than 100 B/s (grace: 10s for TLS negotiation) |
| `MinResponseDataRate` | 100 bytes/sec | Closes connections if response slower than 100 B/s (grace: 10s) |
| `RequestHeadersTimeout` | 15 seconds | HTTP headers must arrive within 15s; prevents header flooding |
| `KeepAliveTimeout` | 120 seconds | Idle connections close after 2 minutes (vs. indefinite hold) |

**Slow Loris Attack Scenario (WITHOUT these limits):**

1. Attacker opens 10,000 connections to the server
2. Each sends 1 byte of HTTP header per 30 seconds
3. Connections never complete, server thread pool exhausted
4. Legitimate requests timeout
5. Server crashes or becomes unresponsive

**Slow Loris Attack Scenario (WITH Kestrel limits):**

1. Attacker opens 10,000 connections, each sends 1 byte per 30 seconds
2. After 10 seconds of grace, Kestrel drops connection (rate < 100 bytes/sec)
3. Thread released for legitimate requests
4. Attack fails

**Monitoring:**

Add metrics to track dropped connections:

```csharp
// Logs appear as:
// warn: Microsoft.AspNetCore.Server.Kestrel[13]
// Connection from 203.0.113.42 was closed because the inactivity timer expired.
```

---

### Request Timeouts (Runaway Query Prevention)

ASP.NET Core Request Timeouts middleware prevents long-running requests from exhausting the thread pool. Different timeout policies apply to different endpoint categories based on expected latency:

**Configuration (Program.cs):**

```csharp
app.UseRequestTimeouts();

// Default policy: 5 seconds (most endpoints)
app.MapRequestTimeouts(new RequestTimeoutPolicy
{
    TimeoutDuration = TimeSpan.FromSeconds(5)
});

// Ingest policy: 10 seconds (allows batch processing)
var ingestPolicy = new RequestTimeoutPolicy
{
    TimeoutDuration = TimeSpan.FromSeconds(10)
};
app.MapPost("/api/v1/logs", PostLogs).WithRequestTimeout(ingestPolicy);

// Query policy: 30 seconds (allows complex searches)
app.MapPost("/api/v1/query/sql", QuerySql).WithRequestTimeout(new RequestTimeoutPolicy
{
    TimeoutDuration = TimeSpan.FromSeconds(30)
});
app.MapPost("/api/v1/query/natural", QueryNatural).WithRequestTimeout(new RequestTimeoutPolicy
{
    TimeoutDuration = TimeSpan.FromSeconds(30)
});
```

**Timeout Response (HTTP 408):**

```json
{
  "error": "RequestTimeout",
  "message": "The request exceeded the timeout duration of 5 seconds.",
  "code": 408
}
```

**Why Per-Endpoint Policies?**

Different operations have different latency profiles:

| Endpoint | Operation | Typical Latency | Policy Timeout |
|----------|-----------|---|---|
| `POST /api/v1/logs` | Ingest batch (100-1000 logs) | 2-8 sec | 10 sec |
| `POST /api/v1/query/sql` | Complex search + aggregation | 5-25 sec | 30 sec |
| `POST /api/v1/query/natural` | AI-generated query + execution | 10-25 sec | 30 sec |
| `GET /health/ready` | Health check | <100 ms | 5 sec (default) |
| `POST /api/v1/auth/keys` | Create API key | <500 ms | 5 sec (default) |

**Runaway Query Scenario (WITHOUT timeouts):**

1. User submits `SELECT * FROM huge_table` without LIMIT
2. Query takes 15 minutes scanning millions of rows
3. Request holds thread pool slot for 15 minutes
4. Other requests queue up behind it
5. Eventually, entire thread pool exhausted
6. Server stops accepting new connections

**Runaway Query Scenario (WITH 30-second timeout):**

1. User submits same query
2. After 30 seconds, request aborts with 408 Timeout
3. Thread released immediately
4. User receives clear error: "Query exceeded 30-second timeout"
5. User optimizes query (adds LIMIT, filters by date range)
6. Other requests unaffected

---

## 7. Compliance Notes

### Audit Log Retention

All SQL queries are stored in the `query-executions` DynamoDB table for **7 years** (configurable):

```json
{
  "queryId": "qry_01HZ...",
  "tenantId": "tenant_01HZ...",
  "userId": "user_01HZ...",
  "sql": "SELECT * FROM logs WHERE year='2026' AND month='03' AND day='24' LIMIT 100",
  "executedAt": "2026-03-24T10:00:00Z",
  "executionTimeMs": 234,
  "costUsd": 0.0012,
  "resultRowCount": 42,
  "tier": "warm"
}
```

**Compliance:** Satisfies SOC 2, GDPR, and HIPAA audit trail requirements.

---

### Encryption at Rest

| Service | Encryption Method |
|---------|------------------|
| **S3** | SSE-S3 (AES-256) |
| **DynamoDB** | AWS-managed encryption (default) |
| **ElastiCache** | Encryption at rest enabled (optional, recommended for HIPAA) |
| **OpenSearch** | Node-to-node encryption + encryption at rest (KMS) |
| **EBS (ECS tasks)** | Encrypted with AWS-managed KMS key |

---

### Encryption in Transit

| Connection | Method |
|------------|--------|
| **Client → ALB** | TLS 1.2+ (ACM certificate) |
| **ALB → ECS** | HTTP (inside VPC, trusted network) |
| **ECS → S3** | HTTPS (TLS 1.2+) |
| **ECS → DynamoDB** | HTTPS (TLS 1.2+) |
| **ECS → OpenSearch** | HTTPS (TLS 1.2+) |
| **ECS → ElastiCache** | TLS in-transit (if enabled) |

**HTTPS Enforcement:** All external-facing endpoints require HTTPS. HTTP requests are redirected to HTTPS.

---

## Next Steps

- See [API Reference](./api-reference.md) for authentication header examples
- See [Runbooks: Incident Response](./runbooks/incident-response.md) for security incident procedures
- See [Query Guide](./query-guide.md) for SQL safety examples
