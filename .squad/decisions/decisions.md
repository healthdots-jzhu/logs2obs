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

Last updated: 2026-03-26T10:15:02Z by Scribe
