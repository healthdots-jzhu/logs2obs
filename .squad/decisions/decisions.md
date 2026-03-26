# Logs2Obs Team Decisions

## Phase 8 Decisions

### Dolores Phase 8 — AI & Graphs
- Extended IAiService with TranslateToSqlAsync + SuggestGraphsAsync while keeping GenerateSqlAsync for compatibility.
- Added NlQueryResult, QueryContext, GraphRenderRequest, and GraphRenderResponse models in Core.
- VegaLiteSpecBuilder/ChartJsConfigBuilder use schema-driven column detection with table/minimal fallbacks; all 9 graph types supported.
- GitHubModelsAiService uses OpenAI-compatible chat/completions with Polly retry, safety validation, and AiQueryAuditLogger persistence under ai-audit:{tenant}:{query}.
- GraphRenderService auto-selects via GraphSuggestionEngine when requested and returns top alternatives.
- Deviation: AI graph suggestions are deferred (SuggestGraphsAsync returns empty list).

### Stubbs Phase 8 Tests
- Added Phase 8 QueryEngine test scaffolding: 7 active GraphSuggestionEngine tests and 38 skipped AI/graph tests.
- Build/test run: 39 passed, 38 skipped (77 total).
- Findings: GraphSuggestionEngine maps single numeric + single row to `Gauge` (not `Stat`) and returns no suggestions for empty schema; VegaLiteSpecBuilder private helpers now return `Dictionary<string, object?>` to satisfy CA1859 analyzers.

### Stubbs Phase 8 Wire Results
- QueryEngine tests: 77/77 passing, 0 skipped.
- Full solution tests (filter FullyQualifiedName!~Adapters.Local): 156/156 passing, 0 skipped.
- Notable API discoveries:
  - GraphRenderService.RenderAsync takes a GraphRenderRequest (QueryId/TenantId/Schema/Results/AutoSelect) and returns GraphRenderResponse with VegaLiteSpec/ChartJsConfig.
  - GitHubModelsAiService parses GitHub Models chat/completions; choice.message.content must be JSON with sql, explanation, suggestedGraphType.

---

## Phase 9 Decisions

### Felix Phase 9 — CI/CD GitHub Actions Workflows

#### deploy.yml — ECR Push + ECS Deployment
- **Authentication:** OIDC over Access Keys via `aws-actions/configure-aws-credentials@v4` with `role-to-assume`; eliminates long-lived AWS access keys in GitHub Secrets. Requires IAM trust policy on the AWS role.
- **Image Tagging:** Dual tags — `:latest` for local testing and `:${github.sha}` for immutable artifact references and rollbacks; ECS can pin to `:sha` in production.
- **ECS Update Strategy:** Use `aws ecs update-service --force-new-deployment` instead of task definition updates; ECS pulls `:latest` tag automatically, simpler and less error-prone.
- **Stability Verification:** Include `aws ecs wait services-stable` after service updates to catch rolling update failures early; adds ~2-5 minutes but acceptable for production safety.
- **Concurrency Control:** `concurrency: { group: deploy-${{ github.ref }}, cancel-in-progress: true }` prevents overlapping deployments and aborts stale deploys.
- **Job Dependency:** `deploy-ecs` depends on `push-images` to ensure images are pushed before triggering ECS service updates.
- **Hardcoded Resource Names:** ECS cluster = `logs2obs-cluster`, services = `logs2obs-{api|worker|puller|queryengine}` (matches CDK ComputeStack naming).
- **Required Secrets:** `AWS_REGION`, `AWS_ROLE_TO_ASSUME`, `ECR_REGISTRY`.

#### release.yml — Tag-Based GitHub Releases
- **Trigger Pattern:** `v*.*.*` tags (semantic versioning); prevents accidental releases from non-version tags.
- **Changelog Generation:** Auto-generate from `git log ${PREV_TAG}..HEAD --pretty=format:"- %s"` (assumes human-readable commit messages); fallback includes all commits if no previous tag.
- **Prerelease Detection:** Auto-detect prerelease by checking for `-rc`, `-beta`, `-alpha` in tag name.
- **Release Tool:** `softprops/action-gh-release@v2` (actively maintained; `actions/create-release@v1` is deprecated).

#### ci.yml — Code Coverage Enhancement
- **Coverage Tool:** `--collect:"XPlat Code Coverage"` with `dotnet test` (Coverlet, standard .NET tool; outputs Cobertura XML for SonarQube/Codecov/etc.).
- **dotnet-coverage Install:** Pre-install `dotnet-coverage` globally before test step for advanced coverage scenarios (merging reports, converting formats).
- **Coverage Results:** `--results-directory ./coverage` for centralized output; simplifies artifact collection with glob pattern.
- **Artifact Upload:** Two separate `actions/upload-artifact@v4` steps — one for `.trx` (test results), one for `coverage.cobertura.xml` (coverage); enables selective download by different tools.
- **Single Test Run:** Replaced separate Test step with single "Test with coverage" step outputting both .trx and coverage; avoids running tests twice.

#### codeql.yml — C# Security Scanning
- **Schedule:** Weekly on Monday 2 AM UTC (`cron: '0 2 * * 1'`) to catch new vulnerabilities in dependencies.
- **Language:** `languages: csharp` (single-language project; cleaner than array syntax).
- **Build Strategy:** `dotnet build logs2obs.slnx -c Release` (explicit build ensures correct .NET 10 / `.slnx` resolution; autobuild may not handle correctly).
- **Permissions:** Minimal scope — `actions: read, contents: read, security-events: write` (principle of least privilege).

---

Last updated: 2026-03-27T00:00:00Z by Scribe
