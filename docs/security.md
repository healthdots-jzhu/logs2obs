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

### JWT Authentication (Cognito)

#### Setting Up Cognito via CDK

logs2obs uses AWS Cognito for JWT-based authentication. Deploy the `AuthStack` in your CDK infrastructure:

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

#### Obtaining a JWT Token

Use the AWS Cognito authentication flow to obtain a JWT:

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

The `NetworkStack` attaches a Web ACL with these managed rule groups:

```csharp
CreateManagedRule("AWSManagedRulesCommonRuleSet", 0),
CreateManagedRule("AWSManagedRulesKnownBadInputsRuleSet", 1)
```

**Additional Custom Rules (recommended):**
- Rate limiting: 2,000 requests/5 minutes per IP
- Geo-blocking: block traffic from untrusted countries
- IP allowlist: restrict administrative endpoints to corporate VPN

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
