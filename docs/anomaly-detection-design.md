# Plausible Anomaly Detection System (ADS) — Architecture & Implementation Package

**Status:** Design / Handoff · **Version:** 1.0 · **Date:** 2026-06-21
**Audience (Part 1):** Product Owners, Business & Solution Architects · **Audience (Part 2+):** Engineering
**Implementation team:** 2 Senior Software Engineers (Python), 1 Elixir/ClickHouse Engineer, 1 Quality Engineer

---

## How to read this document

This single document is the complete design and hand-off package. It is deliberately layered:

| Part | Title | Read it if you are… |
|------|-------|---------------------|
| 1 | Business overview | a Product Owner / business architect — no code, plain language |
| 2 | System architecture | a technical architect — components, reuse decisions, data flow |
| 3 | Architectural Decision Records | reviewing *why* each major choice was made (Option A/B/C) |
| 4 | Detection methodology | reviewing the math/ML rigor |
| 5 | ML lifecycle | cold-start, drift, retraining, registry, A/B, rollback |
| 6 | API design & threat model | building or securing the integration |
| 7 | Capacity, cost & scaling | sizing infrastructure for 1K / 100K / 1M sites |
| 8 | File / module / migration inventory | the exact things to create or change |
| 9 | Work breakdown (task cards) | an engineer picking up a unit of work |
| 10 | Compliance matrix | verifying the package is complete |

**One running example is used everywhere.** Every payload, query, and metric in this document refers to the same fictional tenant so the numbers always tie together:

| Field | Value |
|-------|-------|
| Team | `Acme Inc.` |
| `team.identifier` | `b1ffc0de-0000-4000-8000-000000000001` |
| Site domain | `acme-shop.example` |
| `site_id` (ClickHouse `UInt64`) | `4242` |
| Site timezone | `Europe/London` |
| "Today" | `2026-06-21` |
| Baseline window | `2026-05-24` → `2026-06-20` (28 days, hourly) |
| The anomaly | 1pm `Europe/London` = wire `2026-06-21T12:00:00Z` (`+01:00` is summer DST): visitors = **920**, expected ≈ **138** (CI `[95, 190]`) |
| Anomaly drivers | `visit:source = buy-cheap-followers.ru`, `visit:country = RU`, `visit:device = Mobile`, `visit:browser = Headless Chrome` |
| Privacy leak example | event `Signup`, prop `email = jane.doe@gmail.com`, prop `card_number = 4111111111111111` (Luhn-valid) |
| Returning-visitor example | 27% seen on ≥2 distinct days (within 48h salt windows); BG/NBD 30-day repeat estimate = 0.34 |
| Example finding id | `fnd_2026-06-21_4242_0007` |
| Metrics used throughout | `visitors`, `pageviews`, `bounce_rate`, `visit_duration`, `events` |
| Dimensions used throughout | `visit:source`, `visit:country`, `visit:device`, `visit:browser`, `event:page`, `time:hour` |

---

# Part 1 — Business Overview

## 1.1 What this is, in one paragraph

Plausible already e-mails site owners when traffic **spikes** or **drops** past a number they set by hand (the "Traffic spike/drop" notifications). That feature is blunt: it fires on a fixed threshold, it cannot tell a real surge from referral spam, it cannot see a problem unless *total* traffic moves, and it knows nothing about privacy risk or visitor loyalty. The **Anomaly Detection System (ADS)** is the intelligent successor. It learns each site's *normal* rhythm automatically, flags the unusual, explains *why* it is unusual in plain language, and adds three capabilities Plausible has never had: spotting **mix shifts** (e.g. a sudden flood of bot traffic even when the total looks flat), spotting **privacy data leaks** (e.g. an e-mail address accidentally captured in a custom property), and estimating **returning-visitor loyalty across multiple days** without ever tracking an individual.

## 1.2 The four things ADS detects

1. **Traffic & volume anomalies** — "Your visitors are 6.7× higher than a normal Saturday 1 pm, and it is almost entirely one referral source." Replaces the static-threshold spike/drop e-mail with a self-calibrating one.
2. **Behavioural & composition anomalies** — "Total traffic is normal, but 38% of it is now Headless Chrome (was 1%) — likely a bot." Catches problems invisible to volume alarms.
3. **Privacy data-leak risk** — "An event called `Signup` is sending us what looks like an e-mail address and a credit-card number in a custom property. This is probably a tracking-setup mistake and a compliance risk." ADS redacts the value the instant it sees it and stores only the redacted finding.
4. **Returning-visitor insight (multi-day)** — "About 27% of this week's visitors came back on a second day; we estimate a 34% 30-day repeat rate." Delivered as a *population estimate*, never as individual tracking — see §1.4.

## 1.3 Why it must be self-training

A threshold that is right for `acme-shop.example` on Black Friday is wrong for it on a quiet Tuesday, and wrong for every other site. ADS therefore *learns the baseline per site, per metric, per hour-of-week* and *re-learns continuously* as the site grows or its season changes. The product owner sets a desired **alert budget** ("about one alert per site per week unless something is really wrong"), not a number of visitors. ADS does the math to hit that budget.

## 1.4 The privacy stance (this is a feature, not a limitation)

Plausible is privacy-first and **cookieless**. A visitor's `user_id` is a one-way hash (`SipHash`) of a rotating secret salt plus their IP/user-agent/domain; the salt is **destroyed after 48 hours** (`lib/plausible/session/salts.ex`). After 48 hours the same human produces a *different* `user_id`, so **cross-day re-identification of an individual is impossible by design**. ADS embraces this:

- The returning-visitor feature is **population-level estimation**, not tracking: it observes genuine multi-day repeats *only within the ≤48h window the salt allows*, then uses a well-established statistical model (BG/NBD) to *extrapolate* a population repeat-rate. No individual is followed across days.
- ADS adds a **re-identification-risk alarm** (domain 3b): if a site itself injects a stable, unique-per-visitor identifier that survives salt rotation (e.g. a custom property `user_uuid`), ADS flags it as a privacy regression — the mirror image of the data-leak detector.

## 1.5 Where it lives and what changes for users

ADS is a separate **Python service** that Plausible calls over an internal API. For the site owner nothing changes except that the existing "Email reports" settings page gains a new, smarter **"Anomaly alerts"** toggle, and the alert e-mails become specific and explanatory instead of "traffic is above 10 visitors." The capability is gated behind a feature flag so it can be rolled out gradually.

## 1.6 Business risks & guardrails (plain language)

| Risk | Guardrail |
|------|-----------|
| Alert fatigue (too many e-mails) | Self-calibrating alert *budget*; one-click "this isn't an anomaly" that **immediately** silences similar future alerts. |
| False sense of privacy | Returning-visitor numbers are population estimates with a stated margin of error and a k-anonymity floor; never per-person. |
| Cost runaway as Plausible grows | Classical ML (no GPUs); pre-aggregated feature store; staggered scans. See Part 7. |
| "Black box" distrust | Every alert ships a human-readable explanation and the exact numbers behind it. |

---

# Part 2 — System Architecture

## 2.1 Component map

```
                          PLAUSIBLE (Elixir / Phoenix / ClickHouse / Postgres)            ANOMALY DETECTION SERVICE (Python)
 ┌───────────────────────────────────────────────────────────────────────┐         ┌──────────────────────────────────────────┐
 │  Ingestion (unchanged)                                                  │         │  FastAPI app  (services/anomaly_detection) │
 │   lib/plausible/ingestion/event.ex ── writes ──► events_v2 / sessions_v2│         │   POST /v1/detect                          │
 │                                                                         │         │   POST /v1/train                           │
 │  NEW  ClickHouse feature store (MV)                                     │         │   POST /v1/privacy_scan                    │
 │   priv/ingest_repo/migrations/..._create_ads_visit_features_5m.exs ◄────┼──reads──┤   POST /v1/returning_visitors              │
 │                                                                         │  (train)│   POST /v1/feedback                        │
 │  NEW  Plausible.Workers.AnomalyScan  (Oban, queue :anomaly_scan)        │         │   GET  /healthz /metrics                   │
 │     • builds payload via existing Stats query engine (lib/plausible/    │         │                                            │
 │       stats/query.ex) and the new MV                                    │ detect  │  Detection core:                           │
 │     • calls ADS ──────────────────────────────────────────────────────┼────────►│   point.py composition.py privacy.py       │
 │     • persists findings, sends e-mail                                   │◄────────┤   returning.py scoring.py calibration.py   │
 │                                                                         │ findings│                                            │
 │  NEW  Plausible.AnomalyDetection context + Ecto schemas (Postgres):     │         │  Model registry  (Postgres meta + S3 blob) │
 │     ads_findings, ads_suppression_rules, ads_model_registry            │◄────────┤   reads/writes artifacts in S3 (reuse       │
 │                                                                         │         │   Plausible.S3 bucket)                     │
 │  REUSE  PlausibleWeb.Email + Plausible.Mailer  (alert e-mails)          │         └──────────────────────────────────────────┘
 │  REUSE  Plausible.HTTPClient (Finch) for the outbound call              │
 │  REUSE  FunWithFlags gate; HEEx settings page (toggle); React SPA       │
 │         dashboard assets/js/dashboard/ (in-dashboard insights panel)    │
 └───────────────────────────────────────────────────────────────────────┘
```

## 2.2 Reuse-first inventory — every recommendation tied to existing Plausible code

The brief requires that **each architectural recommendation be tied to a specific existing module/table/worker/component**, and that **each genuinely new component explain why an existing one cannot be reused, citing the path reviewed.**

### 2.2.1 Components we REUSE (no new component introduced)

| Need | Existing Plausible component (reviewed path) | How ADS uses it |
|------|----------------------------------------------|-----------------|
| Read analytics for features | `lib/plausible/stats/query.ex`, `lib/plausible/stats/query_runner.ex`, `lib/plausible/stats/sql/query_builder.ex` | The `AnomalyScan` worker builds the **thin scan trigger** (only the trailing few buckets + current-distribution snapshot) through the **same Query engine** the public `/api/v2/query` uses — no bespoke SQL, correct tenancy/imports for free. The heavy 28–90d baseline read is **not** done here — ADS reads it from the MV at train/cold-start time (ADR-2), keeping the BEAM hot path allocation-light. |
| Schedule periodic detection | Oban cron registered in `config/runtime.exs:824` (`{"*/15 * * * *", Plausible.Workers.TrafficChangeNotifier}`) and the worker pattern in `lib/workers/traffic_change_notifier.ex` | New worker `Plausible.Workers.AnomalyScan` is a sibling worker on a new queue, registered the same way. |
| Send alert e-mails | `lib/plausible_web/email.ex` (`spike_notification/4`, `drop_notification/5`) + `lib/plausible/mailer.ex` (Bamboo) | New `anomaly_notification/4` e-mail function reuses `base_email()`, tags, templates, and `Plausible.Mailer.deliver_now!/1`. |
| Outbound HTTP to ADS | `lib/plausible/http_client.ex` (`Plausible.HTTPClient`, Finch + OpenTelemetry) | The ADS client calls `HTTPClient.post/4`; no new HTTP stack. |
| Store operational state (open findings, suppression, registry, dedup/notify) | Postgres via `Plausible.Repo`; schema/worker patterns in `lib/plausible/site/traffic_change_notification.ex` (table `spike_notifications`) | New Ecto schemas under `lib/plausible/anomaly_detection/`, migrated under `priv/repo/migrations/`. Holds only the **bounded hot set** (ADR-9). |
| Store findings **history** (1B+ rows/yr analytical) | ClickHouse via `Plausible.IngestRepo`/`ClickhouseRepo`; same analytics tier as `events_v2` | New `ads_findings_history` table (append-only) — the dashboard reads history here, not Postgres (ADR-9). Reuses Plausible's existing ClickHouse infra rather than bloating the transactional DB. |
| Per-site / per-team gating | `FunWithFlags` actors defined for `Plausible.Site` (`lib/plausible/site.ex:246`) and `Plausible.Teams.Team` (`lib/plausible/teams/team.ex:6`) | Flag `:anomaly_detection` gates the worker, the settings toggle, and the dashboard panel. |
| Settings toggle UI ("Anomaly alerts") | **Server-rendered HEEx** settings page `lib/plausible_web/templates/site/settings_email_reports.html.heex` + `PlausibleWeb.SiteController.settings_email_reports/2` and its `enable/disable/update_traffic_change_notification` actions | Add the toggle beside the existing spike/drop block — same dead-view + form-POST + redirect pattern; **not** LiveView. |
| In-dashboard insights panel | **React/TS SPA** `assets/js/dashboard/` (esbuild entry `js/dashboard.tsx`, `config/config.exs:19`), `@tanstack/react-query`, `react-router-dom`, shared client `assets/js/dashboard/api.ts`, component pattern `assets/js/dashboard/stats/<feature>/index.tsx` | New `assets/js/dashboard/anomalies/` panel reuses the SPA's contexts, fetch client and UI primitives. The dashboard is React, **not** HEEx/LiveView. |
| Internal JSON API for the SPA | Dashboard's existing internal scope `scope "/api" → scope "/stats", PlausibleWeb.Api`, `pipe_through :internal_stats_api` (`lib/plausible_web/router.ex:272`, already applies `AuthorizeSiteAccess`) | New `GET/POST /api/stats/:domain/anomaly-findings…` live here, exactly like `StatsController` `:sources`/`:pages`. |
| Object storage for model artifacts | `lib/plausible/s3.ex` (already used for analytics exports) | Model blobs stored in a dedicated prefix of the existing bucket; registry metadata in Postgres. |
| Rate limiting the new API surface (Elixir side) | `lib/plausible/rate_limit.ex` (`Plausible.RateLimit`, ETS atomics) | Feedback endpoint and any user-facing ADS endpoint reuse `RateLimit.check_rate/5`. |
| Abuse/threat IP & bot dropping (signal source) | `lib/plausible/shields.ex` + `lib/plausible/ingestion/event.ex` drop-stages (`drop_threat_ip`, `drop_datacenter_ip`) | Composition detector's referral-spam logic complements Shields; Shields rules are read as ground-truth labels for the bot detector. |
| Telemetry/monitoring | `lib/plausible/prom_ex.ex` + `lib/plausible/telemetry/plausible_metrics.ex` | New `[:plausible, :anomaly_detection, *]` telemetry events exported through the existing PromEx plugin. |

### 2.2.2 Components that are NEW — and why reuse is impossible

| New component | Path | Why no existing Plausible component can be reused (path reviewed) |
|---------------|------|-------------------------------------------------------------------|
| **Anomaly Detection Service** (Python/FastAPI) | `services/anomaly_detection/` | Plausible is Elixir/BEAM. Reviewed `lib/plausible/workers/traffic_change_notifier.ex` — detection there is a single `current_visitors >= threshold` integer compare; there is **no** statistical/ML layer, no model artifacts, no scikit-learn/statsmodels equivalent on the BEAM that the team is resourced to maintain. The brief mandates Python for hiring/ecosystem reasons. See ADR-1. |
| **ClickHouse feature store MV** `ads_visit_features_5m` | `priv/ingest_repo/migrations/20260622091000_create_ads_visit_features_5m.exs` | Reviewed `lib/plausible/clickhouse_event_v2.ex` and `priv/data_migrations/.../create-events-v2.sql.eex`: `events_v2` is **raw** (one row per event) and `sessions_v2` is a `VersionedCollapsingMergeTree` requiring `sign`-correct aggregation. Reviewed `imported_*` tables (`priv/ingest_repo/migrations/20211112130238_create_imported_tables.exs`): they are **daily** grain and only populated for sites that import GA — unusable for hourly/5-min native detection. No existing pre-aggregated 5-minute rollup exists, so scanning raw `events_v2` per scan per site is the only alternative, which is the cost we are removing. See ADR-2. |
| **`ads_findings` (PG hot set) / `ads_suppression_rules` / `ads_model_registry`** + **`ads_findings_history` (ClickHouse)** | `priv/repo/migrations/2026062209xxxx_*.exs`; `priv/ingest_repo/migrations/..._create_ads_findings_history.exs` | Reviewed `lib/plausible/site/traffic_change_notification.ex` (table `spike_notifications`): it stores only `{threshold, type, recipients, last_sent}` — cannot represent a scored, multi-domain, explainable finding, a signature-keyed suppression rule, or a model lineage record. PG holds the bounded operational hot set; the 1B+-row/yr **history** goes to ClickHouse (ADR-9) on reused analytics infra, not the transactional DB. |
| **ADS HTTP client + context** | `lib/plausible/anomaly_detection/` | No existing module wraps a Plausible→Python service contract. We reuse `Plausible.HTTPClient` *underneath* but the typed client/context is new. |

## 2.3 Data flow (scheduled detection, the running example)

1. Oban fires `Plausible.Workers.AnomalyScan` (cron, hourly per site shard).
2. Worker checks `FunWithFlags.enabled?(:anomaly_detection, for: site)` and the suppression table.
3. Worker builds a **thin scan trigger** for `site_id = 4242` — only the trailing ≤6 buckets (flush-lag freshness) + the current-window composition snapshot, via a small `Plausible.Stats` query against the MV (not raw SQL, not 28–90d). **No long series is read or serialized on the BEAM hot path** (ADR-2).
4. Worker `POST`s the ~1–2 KB trigger to ADS `/v1/detect` via `Plausible.HTTPClient`.
5. ADS loads the site's model (mmap-cached) whose artifact **embeds the 28–90d baseline** (seasonal profile, residual MAD, composition baselines, calibration quantile), scores the recent points against it (O(K), no STL), fuses scores (Stouffer, §4.5), calibrates against the alert budget (§4.6), and returns ranked, explained anomalies. (A cold-start site with no model yet reads its short history from the MV or uses the population prior — §5.1.)
6. Worker writes rows to `ads_findings`, applies suppression, and for un-suppressed high-severity findings composes `PlausibleWeb.Email.anomaly_notification/4` and delivers via `Plausible.Mailer`.
7. User clicks "Not an anomaly" in the **React dashboard panel** (`assets/js/dashboard/anomalies/`) → SPA `POST`s the internal endpoint → Elixir writes a `ads_suppression_rules` row and `POST`s `/v1/feedback`; ADS raises that signature's threshold immediately (§5.7).

---

# Part 3 — Architectural Decision Records (Option A / B / C)

Each ADR lists three options with Pros, Cons, Cost, Complexity, and Why-rejected for the two not chosen. The **chosen** option is marked ✅.

## ADR-1 — Where does the ML run?

**Decision driver:** the brief mandates Python; we must justify the boundary.

| | **A. Extend Elixir `TrafficChangeNotifier`** | **B. In-BEAM ML with Nx/Axon** | **C. Separate Python service ✅** |
|---|---|---|---|
| Pros | Zero new infra; reuses `lib/workers/traffic_change_notifier.ex` directly | Stays in one runtime; no network hop | Best ML ecosystem (scikit-learn, statsmodels, lifetimes); easiest hiring; independent scaling & deploy; matches brief |
| Cons | No statistical libs; reinventing STL/IsolationForest in Elixir | Nx/Axon young for classical stats; team lacks depth; couples ML deploy to BEAM deploy | Network hop; new service to operate; contract to maintain |
| Cost | Low infra, very high dev cost | Medium infra, high dev cost | Medium infra, low-medium dev cost |
| Complexity | High (algorithmic) | High (tooling) | Medium (operational) |
| Why rejected | Detection quality would be poor and unmaintainable; this is exactly the limitation we are replacing | Forces ML iteration speed down to BEAM release cadence; immature library coverage for STL/BG-NBD | — chosen — |

## ADR-2 — How does ADS obtain analytics data?

**Key realization:** the 28–90 days of history is an input to **training**, not to **detection**. The nightly-trained model (§5.3) already encodes the baseline — seasonal/trend reference, residual median + MAD, calibration quantile, IsolationForest, and the composition baseline distributions. At scan time, detection only needs to *score the latest observation(s)* against that stored model — an O(K) operation over a handful of points. So **no component should pull or serialize 28–90 days on the hot path.**

| | **A. Elixir pushes full 28–90d series every scan** | **B. ADS reads raw `events_v2` directly** | **C. Baseline in the nightly model; hot path scores only recent points; ADS reads the MV for train + cold-start ✅** |
|---|---|---|---|
| Pros | Simple; Elixir owns tenancy | Simple for ADS; no Elixir change | Hot path is O(K) per site (no history); BEAM does ~no large-array work; tiny payloads; high scan frequency is cheap |
| Cons | **At 100K+ sites, serializing/GC-ing a 28–90d hourly JSON array per site per scan creates massive BEAM GC pressure + CPU spikes on the hot path** | Python gets ClickHouse tenancy + `sign`-correctness burden; duplicates `sql/query_builder.ex` | Needs the feature-store MV (T0.3) + read-only CH user; model must persist a compact baseline summary |
| Cost | Low dev / **high runtime at scale** | Low dev / high risk | Low (read-only MV reads are pre-aggregated & cheap, §7) |
| Complexity | Low | Medium | Medium |
| Why rejected | **This is exactly the scaling defect being corrected** — full-series push melts the BEAM under fleet-wide 15-min scans | Re-implements query logic in Python; risks `sign`-incorrect aggregation against `VersionedCollapsingMergeTree` | — chosen — |

**Adopted (C):**
- **Hot detect path:** the Elixir worker (T3.2) sends a **thin scan request** — `{site_id, as_of, recent_points: [last K buckets], distributions_current, alert_budget}` (≈ 1–2 KB). ADS loads the mmap-cached per-site model and scores the recent points against the encoded baseline. **No 28–90d series is read or serialized anywhere on the hot path.**
- **Training path (nightly) + cold start:** ADS reads the pre-aggregated 28–90d window from the feature-store MV `ads_visit_features_5m` through a **read-only CH user with a row policy** (never `events_v2`). `sign`-correctness is handled once, at MV-build time (T0.3). A site with no trained model yet bootstraps from the MV (or the population prior, §5.1) — the only time history is touched at detect time, and it is rare and transient.
- The `recent_points` in the request exist solely to cover ingestion/MV flush lag (the last few minutes) so the freshest bucket isn't missed; they are a handful of values, not a history.

## ADR-3 — Point-anomaly algorithm

| | **A. Static threshold (today)** | **B. Single model (Prophet or STL only)** | **C. Ensemble: STL + robust-z + Isolation Forest, fused by Stouffer ✅** |
|---|---|---|---|
| Pros | Trivial; already in `traffic_change_notifier.ex` | One model to tune | Robust to seasonality + point + contextual anomalies; degrades gracefully; self-training |
| Cons | No seasonality, high false-positive/negative | Prophet heavy & slow; STL alone misses multivariate/contextual | More moving parts |
| Cost | ~0 | Medium (Prophet CPU) | Low-medium (classical, vectorized) |
| Complexity | Low | Medium | Medium-high |
| Why rejected | The exact pain we are replacing | Prophet's per-series fit cost is poor at 1M sites; STL-only is univariate | — chosen — |

## ADR-4 — Model artifact storage & registry

| | **A. Postgres `bytea`** | **B. Local disk on ADS node** | **C. S3 blob + Postgres metadata ✅** |
|---|---|---|---|
| Pros | Transactional with metadata | Fastest load | Reuses `lib/plausible/s3.ex`; durable; shared across replicas; cheap |
| Cons | Bloats Postgres; bad for 1M×blobs | Lost on autoscale; not shared | Eventual consistency; needs cache |
| Cost | High DB | Low but fragile | Low (S3 + small PG rows) |
| Complexity | Low | Low | Medium |
| Why rejected | 1M sites × 200 KB = 200 GB in Postgres is operationally hostile | Breaks the moment ADS scales horizontally (ADR is for ≥100K) | — chosen — |

## ADR-5 — Returning-visitor approach

| | **A. Persistent cross-day visitor IDs** | **B. Privacy-preserving population estimation ✅** | **C. First-party cookie** |
|---|---|---|---|
| Pros | Exact per-user repeat | Honours the 48h salt deletion (`salts.ex`); no individual tracking; defensible to regulators | Exact; common in other tools |
| Cons | **Violates Plausible's core privacy model**; requires storing stable IDs | Estimate, not exact; needs statistical model + k-anon floor | Reintroduces cookies — against product identity |
| Cost | — | Low | — |
| Complexity | — | Medium | — |
| Why rejected | Reviewed `lib/plausible/session/salts.ex`: salts are deleted after 48h precisely to prevent this; building it would dismantle the product's privacy guarantee | — chosen — | Contradicts the cookieless brand promise |

## ADR-6 — ADS deployment topology

| | **A. In-BEAM via Port/NIF** | **B. Sidecar container per Plausible node** | **C. Standalone autoscaled service behind internal LB ✅** |
|---|---|---|---|
| Pros | No network | Co-located, simple discovery | Independent scaling; one model cache; clean blast radius |
| Cons | Couples lifecycles; GIL/port fragility | Wastes RAM (model cache × nodes); scales with web, not with ML load | Needs LB + service discovery |
| Cost | Low | High (RAM duplication) | Medium |
| Complexity | High | Medium | Medium |
| Why rejected | Brittle; defeats the language separation | Model cache duplicated per node; ML load ≠ web load | — chosen — |

## ADR-7 — Feedback loop design

| | **A. Passive logging** | **B. Immediate suppression rules ✅** | **C. Full online learning** |
|---|---|---|---|
| Pros | Simplest | User sees instant effect; bounded, auditable | Continuously adapts |
| Cons | User keeps getting the same bad alert | Coarser than full learning | Risk of feedback-loop instability; hard to audit/rollback |
| Cost | ~0 | Low | High |
| Complexity | Low | Medium | High |
| Why rejected | Alert fatigue persists; erodes trust | — chosen — | Unbounded model drift from user clicks is a support nightmare at launch; revisit post-GA |

## ADR-8 — Inference execution model

| | **A. Synchronous in worker** | **B. ADS-internal async queue** | **C. Sharded batch windows ✅** |
|---|---|---|---|
| Pros | Simple call/response | Smooths bursts | Predictable load; staggered across the hour; matches Oban cron sharding |
| Cons | Worker blocks; thundering herd on the hour | Extra queue infra in Python | Slight latency to first alert |
| Cost | Low | Medium | Low |
| Complexity | Low | Medium | Medium |
| Why rejected | 1M sites hitting ADS at `:00` is a self-inflicted DoS | Adds Celery/RQ we don't otherwise need | — chosen — (shard sites by `site_id % N`, **dripped** across the window by a dispatcher; see §7.3 principle (a)) |

## ADR-9 — Where do anomaly findings live?

**Decision driver:** at 1M sites × ~3 findings/day = **~1.1 billion rows/year**. Keeping that in transactional Postgres and serving the dashboard "anomaly history" from it would wreck read performance, index/vacuum health, and bloat — even partitioned.

| | **A. Postgres only** | **B. ClickHouse only** | **C. Hybrid — Postgres hot set + ClickHouse history ✅** |
|---|---|---|---|
| Pros | One store; transactional with suppression/registry | Built for 1B+ append-only analytical rows + fast scans; matches Plausible's analytics tier | Postgres holds the small mutable operational set (open findings, dedup/notify state); CH holds append-only history for dashboard reads — each store does what it's good at |
| Cons | 1B+ rows destroys dashboard reads, vacuum, index size | Mutable per-finding state (resolved? notified? feedback) is awkward in `*MergeTree`; no FK to sites/suppression | Two stores to write; eventual-consistency between them |
| Cost | High (Postgres bloat at scale) | Medium | Low–medium (CH rows are cheap; PG set is bounded) |
| Complexity | Low | Medium | Medium |
| Why rejected | The exact 1B-row read-performance failure the reviewer flagged | Loses the transactional operational state ADS needs (open/feedback/dedup) | — chosen — PG = bounded hot set (open + last 30d), CH `ads_findings_history` = full append-only history read by the dashboard (T3.4 already reads CH) |

**Adopted (C):** Postgres holds the bounded operational hot set (`ads_findings`: open/unresolved + dedup/notify/feedback state); ClickHouse `ads_findings_history` holds the append-only history. **Postgres is the single transactional source of truth; ClickHouse is an eventually-consistent, denormalized read model** populated by a reliable async sync (ADR-10), and the dashboard reads *only* ClickHouse (ADR-11). Mirrors Plausible's own Postgres-config / ClickHouse-analytics split. *(This explicitly does **not** mean a synchronous dual-write — see ADR-10 for why and how.)*

## ADR-10 — Keeping Postgres and ClickHouse consistent (the dual-write trap)

**Decision driver:** writing synchronously to two stores with no distributed transaction is an anti-pattern. If the Postgres commit succeeds but the ClickHouse insert fails (network blip, part-merge freeze, replica lag), the layers **drift permanently** — the worker logs an error but the Postgres row is already committed, and nothing reconciles it.

| | **A. Synchronous dual-write** | **B. Transactional outbox + idempotent async sync ✅** | **C. CDC (Debezium/Kafka) PG→CH** |
|---|---|---|---|
| Pros | Simplest to write | One synchronous write (PG); CH fed by a retryable, durable, **idempotent** job — partial failure just retries, no drift; reuses Oban + `IngestRepo` already in the stack | Generic, decoupled change capture |
| Cons | **Permanent drift on any partial failure** (the reviewer's exact point) | Eventual consistency (seconds of lag); a sync worker to run | Heavy new infra (Kafka + Debezium + connectors) not otherwise present; operational burden for ~3M rows/day |
| Cost | Low dev / **high integrity risk** | Low | High (infra + ops) |
| Complexity | Low | Medium | High |
| Why rejected | No atomicity across stores → guaranteed drift under real failures | — chosen — | Disproportionate infra for the volume; revisit only if many sinks appear |

**Adopted (B):** the finding is written to Postgres in **one transaction** together with an outbox marker (`ads_findings.ch_synced_at IS NULL`, or a dedicated `ads_findings_outbox`). A reliable Oban worker (`Plausible.Workers.AnomalyFindingsSync`, T3.6) batch-reads un-synced rows, inserts them into `ads_findings_history` **idempotently**, and only then stamps `ch_synced_at`. The CH table is `ReplacingMergeTree` keyed by `finding_id`, so at-least-once re-delivery and later status re-projections (resolve/feedback) collapse to the latest version — duplicates are harmless. If ClickHouse is unavailable, rows simply stay un-synced and retry with backoff; **the two stores converge, never drift.** This is the same async, retryable path Plausible already uses to feed ClickHouse via its write buffers (`lib/plausible/ingestion/write_buffer.ex`).

## ADR-11 — Dashboard findings read model (avoid per-request cross-store stitching)

**Decision driver:** if every dashboard view pulls N rows from ClickHouse, pulls active state from Postgres, and stitches them with nested maps in BEAM memory, read latency degrades for high-volume accounts and adds a cross-DB round-trip per view.

| | **A. Stitch in BEAM per request** | **B. ClickHouse denormalized read model, single query ✅** | **C. Postgres materialized view / read replica** |
|---|---|---|---|
| Pros | No schema change | Dashboard runs **one paginated CH query** returning ready-to-serialize rows (like `StatsController` does for `/api/stats/...`); status is denormalized into the read model by the sync (ADR-10); no PG↔CH join, no BEAM stitching | Familiar SQL |
| Cons | O(rows) nested-map merge in BEAM + 2 DB round-trips per view; degrades at high volume (reviewer's point) | Status freshness is eventually-consistent (seconds) | Re-introduces the 1B-row Postgres problem (ADR-9) |
| Cost | Low dev / high runtime | Low | High |
| Complexity | Low | Medium | Medium |
| Why rejected | Cross-store stitch on the hot read path doesn't scale | — chosen — | Defeats ADR-9 |

**Adopted (B):** `ads_findings_history` carries the denormalized fields the feed needs (kind, metric, severity, explanation headline, signature, **and** current `status`/`feedback`, kept fresh by the ADR-10 sync). The dashboard endpoint (T3.4) issues a **single** ClickHouse query — filter by site + date range, sort, `LIMIT`/paginate — and returns the rows straight through as JSON. Active suppression for the site is a tiny bounded set applied as a query filter (or cached), not a heavy join. Work per view is bounded by the page size (e.g. 50), **independent of total history size**, with zero BEAM-side stitching.

---

# Part 4 — Detection Methodology

This part is the technical core the reviewer should scrutinise hardest. The principle throughout: **adaptive and standardized**, never rigid multipliers.

## 4.1 Feature construction

Per `(site_id, metric)` we build an hourly series over 28 days plus calendar features (hour-of-week, is-holiday). For composition we build, per dimension (`visit:source`, `visit:country`, `visit:device`, `visit:browser`), a categorical distribution over the current window vs the trailing baseline.

## 4.2 Domain 1 — Point / volume anomalies

Three independent detectors run on the univariate series:

1. **STL residual + robust z-score.** Decompose into trend/seasonal/residual (`statsmodels` STL with weekly period). Score = `(residual - median(residual)) / (1.4826 · MAD(residual))` — robust to outliers, unlike mean/σ.
2. **Seasonal expectation interval.** Expected value & prediction interval from the STL seasonal+trend; the example's `expected ≈ 138, CI [95,190]` comes from here.
3. **Isolation Forest** on a multivariate feature vector `[value, hour_of_week_resid, dow, rolling_7d_mean, rolling_7d_std, browser_headless_share, country_top1_share, new_source_share]`. Captures *contextual* anomalies (right count, wrong context).

## 4.3 Domain 2 — Behavioural / composition anomalies (fires at flat total volume)

Composition detectors are **first-class unsupervised detectors**, not just explanation breakdowns:

- **Chi-square / G-test mix-shift** between current and baseline categorical distributions per dimension. Detects the Headless-Chrome `1% → 38%` shift even when `visitors` total is unremarkable.
- **New-entity Poisson tail test.** For a previously-unseen `visit:source` (e.g. `buy-cheap-followers.ru`), model expected count under the historical new-source arrival rate; flag if in the Poisson upper tail.
- **Referrer-spam list** cross-check, complementing `lib/plausible/shields.ex` (Shields deny-rules are read as positive labels).

Each composition anomaly carries `kind ∈ {point, composition, new_entity}`, plus `dimension` and `entity`.

## 4.4 Domain 3 — Privacy data-leak & re-identification risk

- **Leak detector (3a):** scans custom-property keys/values (`meta.key` / `meta.value` from `events_v2`) and entry props (`entry_meta.*`) for: PII regexes (e-mail, phone, IBAN), **Luhn-valid** card numbers, high **Shannon entropy** secrets/tokens, and free-text that looks like names. **Redaction happens in ADS memory before scoring**; only a redacted finding (`email→<redacted:email>`, `4111…1111→<redacted:luhn16>`) is ever persisted.
- **Re-identification-risk detector (3b)** — mirror of 3a: flag a site injecting a custom property that is *unique per visitor* **and** *stable across salt windows* (i.e. it survives the 48h salt deletion). That pattern silently defeats Plausible's anonymity (`salts.ex`) and is reported as a privacy regression.

## 4.5 Joint scoring — standardized multi-signal fusion (not a weighted mean of votes)

Each detector emits a one-sided **p-value** `p_i`. We convert to z-scores `z_i = Φ⁻¹(1 - p_i)` and combine with **weighted Stouffer's Z** with a **dependence correction**, because the detectors read the *same* series and are correlated:

```python
# scoring.py — weighted Stouffer's Z with dependence correction
import numpy as np
from scipy.stats import norm

def joint_score(p_values, weights, rho_hat):
    """p_values, weights: 1D arrays aligned per detector.
    rho_hat: estimated avg pairwise correlation of detector z's (0..1)."""
    z = norm.isf(np.clip(p_values, 1e-12, 1 - 1e-12))      # one-sided z per detector
    w = np.asarray(weights, float)
    k = len(z)
    # Effective denominator inflated by mean pairwise dependence (rho_hat):
    denom = np.sqrt(np.sum(w**2) + 2 * rho_hat * np.sum(np.triu(np.outer(w, w), 1)))
    Z = np.dot(w, z) / denom
    p_joint = float(norm.sf(Z))
    return {"Z": float(Z), "p_joint": p_joint}
```

Fisher's method is retained as an OR-style alternative for "any detector screaming" cases. `rho_hat` is estimated from the historical detector-score covariance per site.

## 4.6 Adaptive calibration — empirical-quantile thresholds to hit an alert budget (not `sensitivity → k`)

We do **not** map a sensitivity slider to a rigid `k·σ`. Instead, per `(site, metric)` we keep the empirical distribution of historical joint scores and pick the threshold at the quantile that yields the target alert rate (the budget), then modulate by feedback:

```python
# calibration.py — budgeted, feedback-modulated threshold
import numpy as np

def calibrated_threshold(baseline_scores, target_alerts_per_week,
                         points_per_week, feedback_inflation=1.0):
    """Pick the score quantile that yields ~target_alerts_per_week,
    then raise it by feedback_inflation (>=1) for suppressed signatures."""
    alert_fraction = min(0.5, target_alerts_per_week / max(points_per_week, 1))
    q = 1.0 - alert_fraction
    base_thr = float(np.quantile(baseline_scores, q))
    return base_thr * feedback_inflation
```

This makes a 6.7× spike on a noisy site and on a quiet site both land near the site's own budget, instead of a one-size threshold.

---

# Part 5 — ML Lifecycle

## 5.1 Cold-start strategy

A brand-new site (or a new metric) has no baseline. Strategy, in order of precedence:

1. **Population prior (hierarchical):** start from a cohort baseline (sites of similar daily volume band & vertical) so detection is conservative-but-live from hour 1.
2. **Widened intervals:** the seasonal prediction interval is inflated by a shrinkage factor `λ(n)` that → 1 as the site accumulates ≥ 14 days of its own data.
3. **Detector gating:** Isolation Forest is disabled until ≥ 7 days exist; only the robust-z + seasonal-interval detectors run, with high thresholds.
4. **No privacy gating:** the privacy leak detector (domain 3a) needs **no history** and runs from the first event — it is stateless regex/entropy/Luhn.

## 5.2 Concept-drift strategy

- **Detection:** Page-Hinkley / ADWIN on the residual stream per `(site, metric)`; a sustained level shift (e.g. a site genuinely grows 3×) triggers a *baseline rebase* rather than a flood of alerts.
- **Distinguish drift from anomaly:** a *persistent* shift over the drift window = drift (rebase); a *transient* spike = anomaly (alert). The drift window is `max(72h, 3× seasonal period)`.
- **Calibration follows drift:** the empirical-quantile threshold (§4.6) is recomputed on the post-rebase window.

## 5.3 Model-retraining strategy

| Trigger | Action | Cadence |
|---------|--------|---------|
| Scheduled | Full per-site refit (STL params, IsolationForest, calibration quantiles, `rho_hat`) | Nightly batch (ADR-8 sharded) |
| Drift detected (§5.2) | Targeted rebase + recalibrate for affected `(site, metric)` | Event-driven |
| Feedback accumulation | Re-derive `feedback_inflation` per signature | Hourly aggregate |
| Global model change | New code version → shadow → A/B (§5.6) | Per release |

## 5.4 Feature store strategy

- **Online (detect):** the Elixir worker materializes features on demand via the Stats query engine + the `ads_visit_features_5m` MV (5-minute grain, `AggregatingMergeTree`), TTL'd (§7).
- **Offline (train):** ADS reads the same MV through the read-only CH user for historical windows.
- **Point-in-time correctness:** features are keyed by `(site_id, bucket_start)`; training never reads buckets newer than the label window, preventing leakage.
- **Schema versioning:** a `feature_schema_version` column lets detectors refuse mismatched vectors.

## 5.5 Model registry

Postgres `ads_model_registry` (lineage) + S3 (artifacts, ADR-4):

```
ads_model_registry(
  id, site_id, model_kind,            -- 'point' | 'composition' | 'returning'
  version, artifact_s3_key,           -- s3://.../ads-models/4242/point/v7.joblib
  feature_schema_version,
  trained_at, training_window_start, training_window_end,
  metrics jsonb,                      -- {"backtest_precision":0.81,"recall":0.74}
  status                              -- 'shadow' | 'candidate' | 'active' | 'rolled_back'
)
```

ADS resolves "the active model for `(site_id=4242, kind=point)`" via this table; artifacts are mmap-cached locally.

## 5.6 A/B testing

- **Shadow mode:** a new model version (`status='shadow'`) runs alongside `active`; its findings are written with a `shadow=true` flag and **not** e-mailed. QE compares precision/recall against the labelled set (§9, T4.2).
- **Canary A/B:** promote to `candidate` for a `site_id % 100 < 5` slice (5%); compare alert-acceptance rate (user "this is useful"/"not an anomaly" feedback) between `active` and `candidate`.
- **Promotion gate:** candidate must show **non-inferior recall** and **lower false-positive rate** with statistical significance before becoming `active`.

## 5.7 Rollback procedures

1. **Instant model rollback:** set the offending version `status='rolled_back'` and the prior `'active'` — ADS picks it up on next resolve (cache TTL ≤ 60s). No redeploy.
2. **Feature flag kill-switch:** `FunWithFlags.disable(:anomaly_detection)` stops all scans globally or per site/team in one call (reuses the existing flag actor on `Plausible.Site`).
3. **Suppression escape hatch:** a global suppression rule `match_all` mutes notifications while leaving findings recorded for debugging.
4. **Schema rollback:** Postgres migrations are reversible (`change/0` with explicit `down`); the MV migration drops cleanly (target table + MV).

---

# Part 6 — API Design & Threat Model

All ADS endpoints are **internal** (Plausible → ADS), authenticated with a service Bearer token modelled on `lib/plausible_web/plugs/authorize_public_api.ex` (SHA-256 hashed, prefixed `plausible-ads-prod_`). The user-facing surface (feedback) is exposed through Plausible's existing authenticated routers, not ADS directly.

For **every** endpoint we list: authentication threats, authorization threats, privacy threats, abuse scenarios, mitigation, and request + response examples.

## 6.1 `POST /v1/detect`

Scores a site's **latest** observations against its nightly-trained model. Per ADR-2, the request is a **thin scan trigger** — it carries only the most recent buckets (to cover ingestion lag) and the *current* composition snapshot. The 28–90d **baseline lives in the model** (and, for cold-start sites, is read by ADS from the feature-store MV). The Elixir worker never serializes a long history onto the BEAM hot path.

> **Timestamp convention (contract):** every machine timestamp on the wire (`as_of`, `recent_points[].t`, response `window`) is a **UTC instant** (ISO-8601 `Z`). Site-local time is conveyed once via `timezone`, used only for hour-of-week features and human-facing `explanation` text. Rationale: feeding STL a per-point local offset that changes across a DST boundary breaks its required evenly-spaced index (see T1.1 / T3.2). In the running example, the 1pm-Europe/London anomaly is `2026-06-21T12:00:00Z` on the wire.

**Request** (≈ 1–2 KB — recent points + current distribution snapshot only)

```json
{
  "site_id": 4242,
  "site_domain": "acme-shop.example",
  "timezone": "Europe/London",
  "as_of": "2026-06-21T13:00:00Z",
  "metrics": {
    "visitors": {
      "granularity": "1h",
      "recent_points": [
        {"t": "2026-06-21T11:00:00Z", "v": 145},
        {"t": "2026-06-21T12:00:00Z", "v": 920}
      ]
    }
  },
  "distributions_current": {
    "visit:browser": {"Chrome": 0.40, "Safari": 0.18, "Headless Chrome": 0.38, "Firefox": 0.04},
    "visit:source":  {"Google": 0.20, "Direct": 0.10, "buy-cheap-followers.ru": 0.70}
  },
  "alert_budget_per_week": 1.0,
  "model_version": "active"
}
```

*(The matching baseline — `expected ≈ 138`, the `visit:browser`/`visit:source` baseline shares, the residual MAD and calibration quantile — is read from the loaded model, not from the request. `recent_points` is capped at a small K, e.g. ≤ 6 buckets, purely for flush-lag freshness.)*

**Response**

```json
{
  "site_id": 4242,
  "as_of": "2026-06-21T13:00:00Z",
  "anomalies": [
    {
      "finding_id": "fnd_2026-06-21_4242_0007",
      "kind": "point",
      "metric": "visitors",
      "window": "2026-06-21T12:00:00Z/PT1H",
      "observed": 920, "expected": 138, "ci": [95, 190],
      "joint_Z": 7.9, "p_joint": 1.4e-15, "severity": "high",
      "drivers": [
        {"dimension": "visit:source", "entity": "buy-cheap-followers.ru", "contribution": 0.71},
        {"dimension": "visit:browser", "entity": "Headless Chrome", "contribution": 0.22}
      ],
      "explanation": "Visitors were 6.7x the expected 138 for Sat 1pm. 71% of the surge came from a single new referrer 'buy-cheap-followers.ru' on Headless Chrome — likely referral spam, not real traffic.",
      "detection_signature": "v:visitors|k:point|sev:high",
      "shadow": false
    },
    {
      "finding_id": "fnd_2026-06-21_4242_0008",
      "kind": "composition", "dimension": "visit:browser", "entity": "Headless Chrome",
      "metric": "visitors", "baseline_share": 0.01, "current_share": 0.38,
      "joint_Z": 6.1, "p_joint": 5e-10, "severity": "medium",
      "explanation": "Headless Chrome jumped from 1% to 38% of traffic while total stayed plausible — a bot signature.",
      "detection_signature": "d:visit:browser|e:Headless Chrome|k:composition",
      "shadow": false
    }
  ],
  "model": {"point": "v7", "composition": "v3"}
}
```

**Threat model**

| Class | Threat | Mitigation |
|-------|--------|------------|
| Authentication | Forged/replayed service token | SHA-256 hashed Bearer (pattern from `authorize_public_api.ex`); mTLS on the internal LB; token in secrets manager, rotated; reject if `Authorization` absent. |
| Authorization | Caller requests `site_id` it doesn't own | ADS is callable **only** by the Plausible service identity; `site_id`↔team ownership is enforced *before* the call in `AnomalyScan` (Elixir holds tenancy). ADS treats `site_id` as opaque and never cross-references other sites. |
| Privacy | Raw PII in `distributions`/series payloads | Payload carries **aggregates only** (counts, shares) — no `user_id`, no IP, no raw prop values. Privacy scanning uses the dedicated `/v1/privacy_scan` with in-memory redaction (§6.3). |
| Abuse | Oversized payload / decompression bomb | Thin contract makes this easy to bound: hard limits ≤ 64 KB body, ≤ 6 `recent_points` per metric, gzip ratio cap; `413`/`422` on exceed. (The old full-series payload is gone — ADR-2.) |
| Abuse | Request flooding (thundering herd) | Sharded scan windows (ADR-8); ADS concurrency cap; internal rate limit per service token. |

## 6.2 `POST /v1/train`

Trains/refits models for a batch of sites; reads features from the MV via the read-only CH user.

**Request**

```json
{
  "site_ids": [4242],
  "kinds": ["point", "composition"],
  "training_window": {"start": "2026-05-01", "end": "2026-06-20"},
  "promote_to": "shadow"
}
```

**Response**

```json
{
  "results": [
    {"site_id": 4242, "kind": "point", "version": "v8",
     "artifact_s3_key": "ads-models/4242/point/v8.joblib",
     "backtest": {"precision": 0.83, "recall": 0.76}, "status": "shadow"}
  ]
}
```

**Threat model**

| Class | Threat | Mitigation |
|-------|--------|------------|
| Authentication | Unauthorized training trigger (cost attack) | Service Bearer only; endpoint not exposed past internal LB. |
| Authorization | Training a site outside the tenant | `site_ids` validated by Elixir before dispatch; ADS read-only CH user has a **row policy** limiting to provided ids. |
| Privacy | Training reads raw events | ADS reads only the **aggregated MV** (no `user_id`/IP columns exist in it); read-only CH credential cannot touch `events_v2`. |
| Abuse | Huge `site_ids` list → resource exhaustion | Batch size cap (≤ 500/req); queued & sharded; per-call CPU budget. |

## 6.3 `POST /v1/privacy_scan`

Scans custom-property samples for leaks; **redacts in memory before responding**.

**Request**

```json
{
  "site_id": 4242,
  "samples": [
    {"event": "Signup", "key": "email", "value": "jane.doe@gmail.com"},
    {"event": "Signup", "key": "card_number", "value": "4111111111111111"},
    {"event": "Pageview", "key": "path", "value": "/checkout"}
  ]
}
```

**Response** (values never echoed back in the clear)

```json
{
  "site_id": 4242,
  "findings": [
    {"event": "Signup", "key": "email", "type": "email",
     "redacted_sample": "j***@***.com", "confidence": 0.99,
     "advice": "Stop sending e-mail addresses as custom properties; hash or remove."},
    {"event": "Signup", "key": "card_number", "type": "pan_luhn16",
     "redacted_sample": "4111********1111", "confidence": 1.0,
     "advice": "Credit-card-like value detected (Luhn-valid). Remove immediately — PCI risk."}
  ],
  "clean_keys": ["path"]
}
```

**Threat model**

| Class | Threat | Mitigation |
|-------|--------|------------|
| Authentication | Token theft → exfiltrate PII via scan | Service Bearer + mTLS; this endpoint is the most sensitive — its access is separately audited. |
| Authorization | Scanning another site's props | `site_id` tenancy enforced by Elixir; ADS holds no cross-site state. |
| Privacy | ADS itself becoming a PII store | **Redact-before-store** invariant: raw values exist only in a request-scoped buffer, never logged, never persisted; responses contain only redacted samples; structured logs scrub by regex. |
| Abuse | Using scan as an oracle to validate stolen cards | Rate-limited; responses give only type+confidence, not validity beyond Luhn; access logged. |

## 6.4 `POST /v1/returning_visitors`

Population-level multi-day repeat estimation (privacy-preserving, §1.4).

**Request**

```json
{
  "site_id": 4242,
  "window": {"start": "2026-06-14", "end": "2026-06-21"},
  "observed_multi_day": {
    "users_seen_1_day": 18230,
    "users_seen_2plus_days_within_48h": 6740,
    "salt_window_hours": 48
  },
  "k_anonymity_floor": 50
}
```

**Response**

```json
{
  "site_id": 4242,
  "observed_multi_day_rate": 0.27,
  "estimated_30d_repeat_rate": 0.34,
  "ci": [0.30, 0.38],
  "method": "BG/NBD extrapolation over 48h salt-bounded observations",
  "k_anonymity_ok": true,
  "caveat": "Population estimate only; cross-day individual tracking is impossible by design (48h salt deletion)."
}
```

**Threat model**

| Class | Threat | Mitigation |
|-------|--------|------------|
| Authentication | Token theft | Service Bearer + mTLS. |
| Authorization | Cross-site estimation | `site_id` tenancy via Elixir. |
| Privacy | Re-identification from small cohorts | **k-anonymity floor**: suppress output if any cohort < k (default 50); only aggregate counts in/out; no `user_id` ever transits. |
| Abuse | Differencing attack across many tiny windows | Minimum window (≥ 7 days); rate-limited; rounded outputs. |

## 6.5 `POST /v1/feedback`

User verdict on a finding → immediate suppression (§5.7).

**Request**

```json
{
  "site_id": 4242,
  "finding_id": "fnd_2026-06-21_4242_0007",
  "verdict": "not_anomaly",
  "scope": "signature",
  "detection_signature": "v:visitors|k:point|sev:high"
}
```

**Response**

```json
{ "status": "suppression_applied", "threshold_inflation": 1.6, "effective_until": "2026-09-21T00:00:00Z" }
```

**Threat model**

| Class | Threat | Mitigation |
|-------|--------|------------|
| Authentication | Forged feedback to blind a site's alerts | Comes only through Plausible's authenticated UI (`PlausibleWeb.AuthPlug` + `AuthorizeSiteAccess`); ADS trusts only the service identity. |
| Authorization | User suppressing a site they can't access | `AuthorizeSiteAccess` plug enforces membership before the Elixir→ADS call. |
| Privacy | Feedback carrying PII | Payload is ids + signature only. |
| Abuse | Mass-suppression to silence real incidents | Suppression is reversible & audited (`ads_suppression_rules` keeps `created_by`); global `match_all` requires admin; rate-limited via `Plausible.RateLimit`. |

## 6.6 `GET /healthz` and `GET /metrics`

Liveness/readiness and Prometheus metrics. **Auth threats:** none for `/healthz` (no data); `/metrics` restricted to the metrics scraper network. **Authorization threats:** `/metrics` could leak per-site cardinality → labels are coarse (no `site_id` in metric labels). **Privacy threats:** none (no PII in metrics). **Abuse:** scrape-only, rate-limited. **Request:** `GET /healthz` → **Response:** `{"status":"ok","models_loaded":3120,"version":"ads-1.0"}`.

---

# Part 7 — Capacity, Cost & Scaling

> **Why this section was rebuilt:** an earlier draft sized everything on **flat per-site averages** (`~15 ms`, `~20 KB/site/day`). That is wrong for web analytics, whose volume, row count, and **cardinality** follow a **power law** — roughly **1% of sites (enterprise "whales") drive ~90–95%** of events, MV rows, and unique-entity cardinality. Averages hide the tail that actually sizes the cluster. The model below is **tiered**, the burst behaviour of a cron-driven system is treated as a first-class load-shaping problem, findings history is moved off transactional Postgres, and the `uniqState` cardinality blow-up is bounded by design. (The BEAM hot-path GC concern is already resolved by the thin-trigger contract — ADR-2 — so all figures below assume the ≤2 KB trigger, not a 28–90d push.)

## 7.1 Workload model — the power law (replaces flat averages)

We model three site tiers. Shares are illustrative but typical of multi-tenant analytics.

| Tier | % of sites | Events/day | Unique entities/dim (cardinality) | Detect CPU/scan | Nightly train CPU | **Feature-MV bytes/day (capped, §7.2)** |
|------|-----------|-----------|-----------------------------------|-----------------|-------------------|------------------------------------------|
| **S — long tail** (hobby/SMB) | ~90% | < 1k | tens | ~1–2 ms | ~0.1 s | ~1–3 KB |
| **M — mid** | ~9% | 10k–500k | hundreds–few k | ~5–10 ms | ~0.5 s | ~30–80 KB |
| **L — whale** (enterprise) | ~1% | 1M–100M+ | 10k–1M+ (URLs, prop values) | ~30–80 ms | ~5–30 s | ~0.5–5 MB |

**Consequence:** at 1M sites (≈ 900k S / 90k M / 10k L), the 10k whales alone produce **~76% of MV storage and ~53% of nightly-train CPU**. Capacity, sharding, and storage must be sized for the **tail**, and the tail must be **isolated** so it cannot starve the long tail (§7.3). Per-tier scan cadence is also a lever: whales scan every 15 min (fast-moving, high-value), the long tail hourly or event-triggered.

## 7.2 Per-resource estimates (tiered)

Assumptions: detect hot path = score ≤6 recent points + 1 capped distribution snapshot against the loaded model (no STL — ADR-2); training nightly; model artifact ~200 KB × 3 versions; **MV per-entity state stored only for bounded dimensions + top-K capping** (see "Feature store" below — this is what keeps Tier L finite).

### Storage growth (tail-dominated)

| Store | 1K sites | 100K sites | 1M sites | Notes |
|-------|----------|-----------|----------|-------|
| Feature MV `ads_visit_features_5m` /day | ~6 MB | ~0.6 GB | **~26 GB/day** (S 1.8 + M 4.5 + L **20**) | Tier L = 76% of it; **30-day TTL → ~790 GB**, 90-day → ~2.4 TB |
| Model artifacts (S3) | ~0.6 GB | ~60 GB | ~600 GB | flat-ish (model size ≪ data size) |
| **Findings — ClickHouse history** (append-only) | ~0.1 GB/yr | ~10 GB/yr | **~1.1 B rows/yr ≈ ~50–100 GB/yr** | analytical store, compresses well, cheap scans — **not** Postgres (ADR-9) |
| **Findings — Postgres hot set** (open/unresolved + dedup state) | < 10 MB | ~0.5 GB | **~2–5 GB bounded** | TTL'd to open + last 30 days; **never** the 1B-row history (ADR-9) |
| Registry/suppression (Postgres) | < 50 MB | ~5 GB | ~50 GB | small mutable rows |

### CPU / inference (no GPU — classical ML; sized by tier, not mean)

| Workload | 1K | 100K | 1M | Note |
|----------|----|------|----|------|
| Detect — **sustained** cores (tier-weighted) | ~0.001 | ~0.07 | **~0.7** | average; **not** the sizing constraint — peak is |
| Detect — **peak** cores at burst (see §7.3) | provision for **~10–20×** sustained | — | — | a cron edge dumps the whole shard at once |
| Nightly train CPU-h/night (tier-weighted) | ~0.08 | ~8 | **~79** (S 25 + M 12 + L **42**) | Tier L = 53%; run as a separate batch fleet |
| Provisioned ADS vCPU (steady-state floor + scheduled burst) | 2 vCPU ×1 | 4 vCPU ×3–6 | 8 vCPU ×**warm floor sized to peak RPS, not mean** | see ADR-8 / §7.3 |

### ClickHouse query cost

MV does the heavy aggregation **at insert time** (incremental `AggregatingMergeTree`). Detect reads are tiny because the baseline lives in the model (ADR-2) — only cold-start/train reads hit the MV. Train read per site = its tier's row count: **S ~hundreds, M ~thousands, L ~10⁵–10⁶ rows** (capped). The whale reads dominate, so **training reads run on a dedicated CH read replica**, off the ingestion/dashboard path, and are scheduled (not concurrent with the dashboard).

### Network traffic (internal, Elixir↔ADS)

| | 1K | 100K | 1M | Note |
|---|----|------|----|------|
| Detect egress/day (gzipped thin trigger) | ~19 MB | ~1.9 GB | ~19 GB | whale triggers are slightly larger (more entities in the capped snapshot) but still KB-scale |

### Model inference cost ($, order-of-magnitude, ARM/c7g-class)

| | 1K | 100K | 1M |
|---|----|------|----|
| ADS serving (warm floor + scheduled burst) | ~$30/mo | ~$600–1,200/mo | ~$5k–10k/mo |
| ADS nightly-train fleet (spot/transient) | ~$5/mo | ~$150/mo | ~$1.5k/mo |
| ClickHouse (MV storage + merges + read replica) | negligible | low–moderate | **dominated by Tier-L cardinality** — budget a dedicated read replica + the MV caps |
| S3 model storage | < $1 | ~$2 | ~$15 |

*"Model inference cost" = CPU only (classical ML, no GPU/tokens). Optional LLM explanations would add per-call cost; default explanations are templated and free (T2.5).*

## 7.3 Cloud Infrastructure Power-Growth Strategy

Three cross-cutting principles, applied at every stage:

**(a) Flatten the burst — don't autoscale into it.** A cron-driven system is *violently bursty*: at `:00` the scheduler would otherwise enqueue the entire shard at once. Reactive autoscaling (CPU/queue-depth) **cannot** respond in time — a new replica cold-starts in minutes, the burst lasts seconds. So burst is shaped, not chased: a **dispatcher drips work** across the whole interval (deterministic `site_id`-keyed minute-bucketing + a token-bucket enqueue rate), giving ADS a **steady RPS** instead of a spike. Autoscaling then only tracks slow **diurnal** trends, backed by **scheduled scaling** aligned to known peaks and a **warm capacity floor sized to peak steady-state RPS, not the mean**.

**(b) Isolate the tail.** Whales (Tier L) get a **dedicated Oban queue (`:anomaly_scan_whales`) and dedicated ADS replicas**, bin-packed apart from the long tail, so one 100M-event/day site cannot head-of-line-block thousands of small sites. Per-tier cadence and per-tier CPU limits prevent a single whale from monopolizing a worker.

**(c) Right store for the job — without the dual-write trap.** Findings **history** is append-only analytical data → **ClickHouse** (`ads_findings_history`, the dashboard's read model); Postgres keeps only the small, mutable **operational** set (open findings, dedup/notify/feedback, suppression) and is the **single transactional source of truth**. The two are kept consistent by a **transactional outbox + idempotent async sync worker** (ADR-10) — never a synchronous dual-write — and the dashboard reads **only** ClickHouse via a single paginated query, never stitching across stores in BEAM (ADR-11). Same Postgres-vs-ClickHouse split Plausible already lives by (ADR-9).

### Stage 1 — 1,000 sites (pilot / single region)
- **1 ADS instance** (2 vCPU / 4 GB), models on S3 mmap-cached. Single `:anomaly_scan` queue; dispatcher drips across the interval even here (cheap habit that scales).
- Reuse existing ClickHouse & Postgres; MV < 10 MB/day. No read replica yet. Findings history table created in ClickHouse from day one (ADR-9) so the read path never has to be retrofitted.

### Stage 2 — 100,000 sites (GA / multi-AZ)
- **3–6 ADS replicas** (4 vCPU / 8 GB) behind an **internal LB**; **dispatcher** spreads scans across the interval (burst-flattening, principle a); **dedicated whale queue + replica** (principle b).
- **Dedicated ClickHouse read replica** for training + dashboard-history reads; MV with 30–90-day TTL.
- **Findings**: ClickHouse history + bounded Postgres hot set (principle c); dashboard reads history from CH.
- **Model cache**: local mmap + S3 source of truth, TTL ≤ 60 s (instant rollback). PromEx alerts on detect p99, **per-tier** queue depth, and dispatcher drip-rate saturation.

### Stage 3 — 1,000,000 sites (hyperscale / multi-region)
- **Per-region ADS fleets** co-located with regional ClickHouse; **scheduled scaling** to the diurnal curve + a warm floor sized to peak RPS (principle a) — autoscaling absorbs only slow trends, never the cron edge.
- **Tail isolation at scale**: a fleet of whale-only replicas with CPU caps + per-tier cadence; the 10k whales are bin-packed and may scan every 15 min while the 900k long-tail sites scan hourly/event-triggered.
- **Feature store**: 30-day MV TTL + downsample older buckets to hourly; **mandatory cardinality caps** (top-K + "other", bounded `uniqCombined`, no per-entity state for unbounded dims) — without them, Tier-L MVs are *unbounded* (the original flat estimate's core error). Consider S3-backed (tiered) ClickHouse disks for cold MV partitions.
- **Findings**: ClickHouse `ads_findings_history` (1B+ rows/yr lives here comfortably; monthly partitions + TTL + optional rollup of resolved findings); Postgres hot set stays in the low-GBs.
- **Training**: separate sharded **batch fleet** (spot instances) running a few hours nightly; whale training is scheduled into its own window so it never collides with the dashboard read replica.
- **Backpressure & graceful degradation**: if ADS saturates, the dispatcher sheds/queues low-tier scans first and the worker falls back to the legacy static-threshold path (`TrafficChangeNotifier` logic) for affected sites — degraded, not down.

---

# Part 8 — File / Module / Migration Inventory

### New files — Python service (`services/anomaly_detection/`)

```
services/anomaly_detection/
  pyproject.toml                       # package: plausible-ads
  app/
    main.py                            # FastAPI app, routers
    auth.py                            # service Bearer verification (SHA-256)
    schemas.py                         # pydantic request/response models (mirror /v1 contract)
    config.py                          # settings (CH read-only DSN, S3, budgets)
    detectors/
      point.py                         # STL + robust-z + IsolationForest
      composition.py                   # chi-square / G-test + new-entity Poisson + spam list
      privacy.py                       # regex + entropy + Luhn, redact-in-memory
      returning.py                     # BG/NBD population estimation + k-anon floor
    scoring.py                         # weighted Stouffer Z + dependence correction
    calibration.py                     # budgeted empirical-quantile thresholds
    registry.py                        # Postgres meta + S3 artifact load/save (joblib)
    feature_store.py                   # read-only ClickHouse MV reader
    training.py                        # nightly refit + backtest + promote_to
    explain.py                         # templated explanations
    telemetry.py                       # prometheus-client metrics
  tests/                               # pytest (owned with QE)
  Dockerfile
```
**Python packages:** `fastapi`, `uvicorn[standard]`, `pydantic`, `numpy`, `pandas`, `scipy`, `scikit-learn`, `statsmodels`, `lifetimes` (BG/NBD), `joblib`, `clickhouse-connect`, `boto3`, `prometheus-client`, `httpx`, `structlog`. *(Optional: `presidio-analyzer` for richer PII — default impl is dependency-free regex/entropy/Luhn.)*

### New files — Elixir client (`lib/plausible/anomaly_detection/`)

```
lib/plausible/anomaly_detection.ex                 # Plausible.AnomalyDetection (context)
lib/plausible/anomaly_detection/client.ex          # Plausible.AnomalyDetection.Client (wraps HTTPClient)
lib/plausible/anomaly_detection/finding.ex         # Ecto schema -> ads_findings
lib/plausible/anomaly_detection/suppression_rule.ex# Ecto schema -> ads_suppression_rules
lib/plausible/anomaly_detection/model_registry.ex  # Ecto schema -> ads_model_registry
lib/workers/anomaly_scan.ex                         # Plausible.Workers.AnomalyScan (Oban, queue :anomaly_scan)
lib/workers/anomaly_findings_sync.ex                # Plausible.Workers.AnomalyFindingsSync (outbox→ClickHouse, ADR-10)
lib/plausible_web/controllers/api/anomaly_controller.ex  # PlausibleWeb.Api.AnomalyController (SPA-facing JSON)
```

### New files — React/TypeScript dashboard panel (`assets/js/dashboard/anomalies/`)

> The dashboard is a React 18/TS SPA (esbuild entry `js/dashboard.tsx`), **not** HEEx/LiveView — these are the front-end files for the in-dashboard insights panel (Task T3.5).

```
assets/js/dashboard/anomalies/index.tsx            # panel entry, wired into router.tsx / nav
assets/js/dashboard/anomalies/anomaly-list.tsx     # react-query list of findings
assets/js/dashboard/anomalies/anomaly-card.tsx     # one finding + explanation + severity
assets/js/dashboard/anomalies/feedback-buttons.tsx # "Useful" / "Not an anomaly" (optimistic mutation)
assets/js/dashboard/anomalies/fetch-anomalies.ts   # uses api.ts + url.apiPath(site, '/anomaly-findings')
```

### Files to MODIFY (exact)

| File | Change |
|------|--------|
| `config/runtime.exs` | Add `{"7 * * * *", Plausible.Workers.AnomalyScan}` and `{"*/2 * * * *", Plausible.Workers.AnomalyFindingsSync}` to `base_cron` (~line 824, beside `TrafficChangeNotifier`); add `:anomaly_scan`, `:anomaly_scan_whales`, **and `:anomaly_findings_sync`** Oban queues; add `:anomaly_detection` config block (ADS base URL, token, budgets, drip rate, tier thresholds, sync interval). |
| `lib/plausible_web/email.ex` | Add `anomaly_notification/4` (mirrors `spike_notification/4`, lines ~149). |
| `lib/plausible_web/router.ex` | Add `get/post "/:domain/anomaly-findings…"` inside the existing `scope "/api" → scope "/stats", PlausibleWeb.Api` block (`pipe_through :internal_stats_api`, ~line 272) — same pipeline as `:sources`/`:pages`, so `AuthorizeSiteAccess` already applies. |
| `lib/plausible_web/templates/site/settings_email_reports.html.heex` | Add the "Anomaly alerts" toggle/recipients/budget block beside the existing spike/drop block (**server-rendered HEEx**, not LiveView/React). |
| `lib/plausible_web/controllers/site_controller.ex` | Add `enable/disable/update_anomaly_alerts` actions mirroring `enable/disable/update_traffic_change_notification`; thread the flag into `settings_email_reports/2`'s render assigns. |
| `assets/js/dashboard/router.tsx` (+ a nav entry) | Register the new `/anomalies` panel route in the SPA. |
| `lib/plausible/prom_ex.ex` | Register `[:plausible, :anomaly_detection, :*]` telemetry. |

### New migrations (exact names)

```
priv/repo/migrations/20260622090000_create_ads_findings.exs            # PG: bounded hot set (open/recent)
priv/repo/migrations/20260622090100_create_ads_suppression_rules.exs
priv/repo/migrations/20260622090200_create_ads_model_registry.exs
priv/ingest_repo/migrations/20260622091000_create_ads_visit_features_5m.exs   # CH: target table + MV (cardinality-capped)
priv/ingest_repo/migrations/20260622091100_create_ads_findings_history.exs    # CH: append-only findings history (ADR-9)
```

---

# Part 9 — Work Breakdown (Task Cards)

**Engineers:** **SSE-A** (Senior SWE, Python — detection core) · **SSE-B** (Senior SWE, Python — service/privacy/returning/serving) · **ECE** (Elixir/ClickHouse) · **QE** (Quality).

## Dependency graph & sequencing

```
              ┌──────────────────────── Phase 0: CONTRACT FREEZE (unblocks all) ───────────────────────┐
   T0.1 (SSE-B) OpenAPI + JSON schemas + mock ADS server
   T0.2 (ECE)   Postgres migrations + Ecto schemas
   T0.3 (ECE)   ClickHouse MV + read-only CH user
              └───────────────┬──────────────────────────┬───────────────────────────┬────────────────┘
                              │                           │                           │
        Phase 1 (PARALLEL, all build against the frozen contract + mock):
        SSE-A: T1.1 point → T1.2 composition → T1.3 scoring+calibration
        SSE-B: T2.1 service skeleton → T2.2 privacy → T2.3 returning → T2.4 train/registry → T2.5 feedback/explain
        ECE:   T3.1 ADS client → T3.2 AnomalyScan worker → T3.3 notify+feedback(outbox) → T3.6 CH sync worker → T3.4 HEEx settings + JSON API
        FE:    T3.5 React dashboard insights panel (needs T3.4 API; skill-gap — see card; off critical path)
        QE:    T4.1 contract tests (needs T0.1) → T4.2 quality harness → T4.3 privacy/load/E2E
                              │
        Phase 2 INTEGRATION:  SSE-A+SSE-B wire real detectors into service; ECE points worker at real ADS; QE runs E2E.
                              │
        Phase 3 ROLLOUT:      shadow (T2.4/§5.6) → canary A/B → calibrate (§4.6) → GA via flag.
```

**Critical lever:** Phase 0 freezes the contract (OpenAPI + mock + CH payload + Postgres schema) so all four engineers build in parallel against mocks — no engineer waits on another's implementation, only on the *contract*.

---

### Task T0.1 — Freeze the ADS API contract (OpenAPI + JSON Schemas + mock server)

**Objective:** Produce the authoritative OpenAPI 3.1 spec and runnable mock server for all `/v1` endpoints so every engineer codes against a stable contract from day one.

**Dependencies:** None. Blocks T1.*, T2.*, T3.1, T4.1.

**Input Example:**
```yaml
# contract/openapi.yaml (excerpt) — the /v1/detect request body
DetectRequest:
  type: object
  required: [site_id, metrics]
  properties:
    site_id: {type: integer, example: 4242}
    site_domain: {type: string, example: "acme-shop.example"}
    metrics:
      type: object
      additionalProperties:
        $ref: '#/components/schemas/MetricSeries'
    alert_budget_per_week: {type: number, default: 1.0}
```

**Output Example:**
```json
// mock server response for POST /v1/detect (matches Part 6.1 exactly)
{"site_id":4242,"as_of":"2026-06-21T13:00:00Z","anomalies":[
  {"finding_id":"fnd_2026-06-21_4242_0007","kind":"point","metric":"visitors",
   "observed":920,"expected":138,"ci":[95,190],"joint_Z":7.9,"severity":"high"}],
 "model":{"point":"v7","composition":"v3"}}
```

**Acceptance Criteria:**
- `openapi.yaml` validates (Spectral lint, 0 errors) and covers all 6 endpoints in Part 6.
- Mock server (`prism mock` or FastAPI stub) returns the Part-6 example payloads for the running example.
- Schemas published to the repo so SSE/ECE/QE import them; any field rename requires a contract PR.

**Key Code Snippet:**
```python
# contract/mock_main.py — FastAPI stub returning canned running-example payloads
from fastapi import FastAPI
app = FastAPI(title="ADS mock")

@app.post("/v1/detect")
def detect(body: dict):
    return {"site_id": body["site_id"], "as_of": body.get("as_of"),
            "anomalies": [{"finding_id": "fnd_2026-06-21_4242_0007", "kind": "point",
                           "metric": "visitors", "observed": 920, "expected": 138,
                           "ci": [95, 190], "joint_Z": 7.9, "severity": "high"}],
            "model": {"point": "v7", "composition": "v3"}}
```

**Required Skills:** OpenAPI 3.1 authoring; FastAPI; API design.

**Required Knowledge:** The four detection domains' I/O shapes; Plausible metric/dimension vocabulary (`visit:source`, `event:page`, …).

**Self-Learning References:** OpenAPI spec (`https://spec.openapis.org/oas/v3.1.0`); FastAPI (`https://fastapi.tiangolo.com/`); Stoplight Prism mocking (`https://github.com/stoplightio/prism`); Plausible Stats API schema (repo `priv/json-schemas/query-api-schema.json`).

**Estimated Effort:** 3 days.

**Risks:** Contract churn after Phase 1 starts → mitigate by versioning the contract and requiring a PR + sign-off from all four roles for changes.

---

### Task T0.2 — Postgres migrations & Ecto schemas (findings, suppression, registry)

**Objective:** Create the three Postgres tables and Ecto schemas that hold ADS **operational** state on the Plausible side, modeled on `spike_notifications`. Per ADR-9, `ads_findings` in Postgres is the **bounded hot set** (open/unresolved findings + dedup/notification state, TTL'd to open + last 30 days) — the 1B+-row/yr analytical **history** lives in ClickHouse `ads_findings_history` (T0.3), not here.

**Dependencies:** T0.1 (field names). Blocks T3.2, T3.3, T2.4 (registry shape). Pairs with T0.3 (ClickHouse history table).

**Input Example:**
```elixir
# priv/repo/migrations/20260622090000_create_ads_findings.exs
defmodule Plausible.Repo.Migrations.CreateAdsFindings do
  use Ecto.Migration
  def change do
    create table(:ads_findings) do
      add :finding_id, :string, null: false
      add :site_id, references(:sites, on_delete: :delete_all), null: false
      add :kind, :string, null: false            # point | composition | new_entity | privacy | returning
      add :metric, :string
      add :severity, :string
      add :observed, :float
      add :expected, :float
      add :joint_z, :float
      add :detection_signature, :string
      add :explanation, :text
      add :payload, :map                          # jsonb: drivers, ci, window
      add :shadow, :boolean, default: false
      add :notified_at, :naive_datetime
      add :status, :string, default: "open"       # open | resolved | not_anomaly
      add :ch_synced_at, :naive_datetime          # transactional-outbox marker (ADR-10); NULL = not yet in ClickHouse
      timestamps()
    end
    create unique_index(:ads_findings, [:finding_id])
    create index(:ads_findings, [:site_id, :inserted_at])
    create index(:ads_findings, [:ch_synced_at])  # partial-scan target for the sync worker (T3.6)
  end
end
```

**Output Example:**
A persisted row, as an Elixir map:
```elixir
%Plausible.AnomalyDetection.Finding{
  finding_id: "fnd_2026-06-21_4242_0007", site_id: 4242, kind: "point",
  metric: "visitors", severity: "high", observed: 920.0, expected: 138.0, joint_z: 7.9,
  detection_signature: "v:visitors|k:point|sev:high",
  explanation: "Visitors were 6.7x the expected 138 for Sat 1pm...",
  payload: %{"ci" => [95, 190], "drivers" => [%{"dimension" => "visit:source",
             "entity" => "buy-cheap-followers.ru", "contribution" => 0.71}]},
  shadow: false, notified_at: nil
}
```

**Acceptance Criteria:**
- `mix ecto.migrate` and rollback both succeed; `down` drops cleanly.
- All three schemas (`Finding`, `SuppressionRule`, `ModelRegistry`) have changesets with `validate_required` and the unique constraints listed.
- `ads_suppression_rules` supports `match_all`, signature-scoped, and entity-scoped rules with `effective_until` and `created_by`.

**Key Code Snippet:**
```elixir
# lib/plausible/anomaly_detection/suppression_rule.ex
defmodule Plausible.AnomalyDetection.SuppressionRule do
  use Ecto.Schema
  import Ecto.Changeset
  schema "ads_suppression_rules" do
    field :scope, Ecto.Enum, values: [:match_all, :signature, :entity]
    field :detection_signature, :string
    field :threshold_inflation, :float, default: 1.5
    field :effective_until, :naive_datetime
    field :created_by, :integer
    belongs_to :site, Plausible.Site
    timestamps()
  end
  def changeset(s, a), do:
    s |> cast(a, [:site_id, :scope, :detection_signature, :threshold_inflation, :effective_until, :created_by])
      |> validate_required([:site_id, :scope])
end
```

**Required Skills:** Ecto migrations & schemas; Postgres indexing; jsonb columns.

**Required Knowledge:** Plausible's `Plausible.Repo` conventions; the `spike_notifications` schema (`lib/plausible/site/traffic_change_notification.ex`) as the model.

**Self-Learning References:** Ecto migrations (`https://hexdocs.pm/ecto_sql/Ecto.Migration.html`); Ecto schema (`https://hexdocs.pm/ecto/Ecto.Schema.html`); Postgres jsonb (`https://www.postgresql.org/docs/current/datatype-json.html`).

**Estimated Effort:** 2.5 days.

**Risks:** Findings table growth at scale → mitigate with monthly partitioning + TTL (Part 7) introduced before 100K stage.

---

### Task T0.3 — ClickHouse feature-store MV `ads_visit_features_5m` + read-only CH user

**Objective:** Create the 5-minute pre-aggregated materialized view (target table + MV) feeding train/cold-start reads, **with mandatory cardinality controls** so high-traffic (Tier-L) sites cannot blow up the `AggregatingMergeTree` state; the append-only `ads_findings_history` ClickHouse table for findings history (ADR-9); and a restricted read-only ClickHouse user with a row policy for ADS.

**Dependencies:** T0.1. Blocks T2.4 (train reads), T3.2 (cold-start reads), T3.4 (dashboard reads findings history).

**Input Example:**
Source rows from `events_v2` (the running example):
```text
events_v2: site_id=4242, name='pageview', timestamp='2026-06-21 13:02:11',
           browser='Headless Chrome', referrer_source='buy-cheap-followers.ru',
           country_code='RU', screen_size='Mobile', user_id=14952786533720156842
(+ ~919 sibling rows in the 13:00 bucket)
```

**Output Example:**
MV target row after aggregation:
```text
ads_visit_features_5m:
  site_id=4242, bucket_start='2026-06-21 13:00:00',
  visitors_state=<AggregateFunction uniq(user_id)>,    -- finalizes to 920
  pageviews=931,
  dim='visit:browser', entity='Headless Chrome', entity_visitors=349,
  feature_schema_version=1
```

**Acceptance Criteria:**
- MV populates incrementally on insert; `FINAL`-free reads via `-Merge` combinators are `sign`-correct for session-derived metrics.
- **Cardinality is bounded (the core fix for Tier-L blow-up):** per-entity state is stored **only for bounded dimensions** (`visit:browser`, `visit:os`, `visit:device`, `visit:country`, `visit:channel`, top-K `visit:source`); **unbounded dimensions** (`event:page`/URL, custom-property values) are **never** broken down per-entity in this MV — they are stored as **totals only**, or as a separate top-K (e.g. K=50) + an `"other"` rollup. Unique counts use **bounded `uniqCombined(12)`** (stable ~KB state), not exact `uniq`.
- A Tier-L stress test (a synthetic site with 500k unique pages + 100k unique prop values) produces a **bounded** MV footprint (≤ low-MB/day) and does **not** create per-entity rows for the unbounded dims.
- Reading a Tier-S/M site's window scans ≤ ~10k rows, < 10 ms; Tier-L reads run on the dedicated read replica.
- `ads_findings_history` (ClickHouse, `ReplacingMergeTree(version)`, ORDER BY `(site_id, finding_id)`) exists; idempotent on re-insert by `finding_id` (verified by inserting the same finding twice → one logical row after merge / `FINAL`); carries denormalized `status`/`explanation_headline` so the dashboard (T3.4) reads it with no PG join; written only by the sync worker (T3.6), not by a synchronous dual-write.
- Read-only user `ads_reader` can `SELECT` only `ads_visit_features_5m` + `ads_findings_history`, constrained by a row policy; cannot read `events_v2`/`sessions_v2`.
- Migration `down` drops MV then target tables.

**Key Code Snippet:**
```sql
-- priv/ingest_repo/migrations/20260622091000_create_ads_visit_features_5m.exs (SQL it runs)
CREATE TABLE ads_visit_features_5m (
  site_id UInt64, bucket_start DateTime, dim LowCardinality(String),
  entity String,
  visitors_state AggregateFunction(uniqCombined(12), UInt64),  -- BOUNDED HLL (~KB), not exact uniq
  pageviews SimpleAggregateFunction(sum, UInt64), feature_schema_version UInt8
) ENGINE = AggregatingMergeTree()
PARTITION BY toYYYYMM(bucket_start)
ORDER BY (site_id, bucket_start, dim, entity)
TTL bucket_start + INTERVAL 30 DAY;

-- ONLY bounded dims get a per-entity breakdown. Unbounded dims (page/URL, prop values)
-- are NOT enumerated here (that is the Tier-L row explosion). They get totals-only,
-- or a separate top-K MV with an "other" rollup.
CREATE MATERIALIZED VIEW ads_visit_features_5m_mv TO ads_visit_features_5m AS
SELECT site_id, toStartOfFiveMinute(timestamp) AS bucket_start,
       'visit:browser' AS dim, browser AS entity,
       uniqCombinedState(12)(user_id) AS visitors_state,
       sumSimpleState(1) AS pageviews, 1 AS feature_schema_version
FROM events_v2
WHERE browser != ''
GROUP BY site_id, bucket_start, dim, entity;   -- repeat per BOUNDED dim only

-- Findings history (ADR-9): 1B+ rows/yr live here, NOT in Postgres.
-- ReplacingMergeTree(version) so at-least-once re-delivery from the outbox sync
-- (ADR-10) AND later status re-projections (resolve/feedback) collapse by finding_id
-- to the latest version. Denormalized status/headline make it the dashboard read
-- model (ADR-11) — no PG join at read time.
CREATE TABLE ads_findings_history (
  site_id UInt64, finding_id String, detected_at DateTime, kind LowCardinality(String),
  metric LowCardinality(String), severity LowCardinality(String),
  observed Float64, expected Float64, joint_z Float32, detection_signature String,
  explanation_headline String,
  status LowCardinality(String) DEFAULT 'open',   -- open|resolved|not_anomaly (denormalized)
  feedback LowCardinality(String) DEFAULT '',
  shadow UInt8,
  version UInt64                                   -- monotonically increasing per finding_id
) ENGINE = ReplacingMergeTree(version)
PARTITION BY toYYYYMM(detected_at)
ORDER BY (site_id, finding_id)                     -- dedups/collapses by finding_id
TTL detected_at + INTERVAL 2 YEAR;

CREATE ROW POLICY ads_reader_policy ON ads_visit_features_5m USING 1 TO ads_reader;
```

**Required Skills:** ClickHouse DDL; materialized views; `AggregatingMergeTree` & `-State`/`-Merge` combinators; `VersionedCollapsingMergeTree` `sign` semantics.

**Required Knowledge:** Plausible's `events_v2`/`sessions_v2` schema (`lib/plausible/clickhouse_event_v2.ex`, `clickhouse_session_v2.ex`); ingest_repo migration pattern (`priv/ingest_repo/migrations/`); the `sign` aggregation rule for sessions; **ClickHouse cardinality behaviour** — why `uniq`/per-entity `GROUP BY` on unbounded dimensions explodes `AggregatingMergeTree` state/rows, and how `uniqCombined(p)` bounds it; top-K + "other" patterns.

**Self-Learning References:** ClickHouse MV (`https://clickhouse.com/docs/en/materialized-view`); AggregatingMergeTree (`https://clickhouse.com/docs/en/engines/table-engines/mergetree-family/aggregatingmergetree`); `uniqCombined` vs `uniq` bounded state (`https://clickhouse.com/docs/en/sql-reference/aggregate-functions/reference/uniqcombined`); CollapsingMergeTree/`sign` (`https://clickhouse.com/docs/en/engines/table-engines/mergetree-family/versionedcollapsingmergetree`); ClickHouse row policies (`https://clickhouse.com/docs/en/sql-reference/statements/create/row-policy`).

**Estimated Effort:** 7 days (was 5; +2 for cardinality controls + the findings-history table).

**Risks:**
- **Cardinality blow-up on Tier-L sites (high):** breaking down an unbounded dim (page/URL, prop values) per-entity per 5-min bucket creates millions of rows/site/day and balloons the `AggregatingMergeTree` state — the flat "20 KB/site/day" estimate's core error. Mitigation: bounded `uniqCombined(12)`, per-entity state for **bounded dims only**, top-K + "other" for the rest, TTL + downsampling; verified by the Tier-L stress test above.
- **Session-metric `sign` correctness:** `bounce_rate`/`visit_duration` aggregation must respect `sign` from `sessions_v2`; getting it wrong silently corrupts baselines → parity test (T4.1) comparing MV output to the Stats query engine for `site_id=4242`.

---

### Task T1.1 — Point/volume detector (`detectors/point.py`)

**Objective:** Implement the three point detectors (STL robust-z, seasonal interval, Isolation Forest) emitting per-detector one-sided p-values. Each detector has two code paths: a **`fit()`** path (nightly, batch — runs STL over the 28–90d window read from the MV and stores a compact baseline: seasonal/trend reference, residual median + MAD, IsolationForest, calibration quantile) and a **`score()`** path (hot, per scan — scores only the latest `recent_points` against that stored baseline, **no STL per scan**). This fit/score split is what lets the detect path stay O(K) and keeps the BEAM payload thin (ADR-2).

**Dependencies:** T0.1 (schema). Mockable; no live ADS needed. `fit()` consumes the MV history (T0.3); `score()` consumes the thin request (§6.1).

**Input Example:**
```python
# CONTRACT: series timestamps are UTC instants (suffix "Z"). The site-local
# offset (Europe/London = +01:00 in summer) is NOT encoded per-point — it is
# carried once in the payload's `timezone` field (used only for hour-of-week
# features and human display). This is required so STL gets an evenly-spaced
# index even when the 28-day window crosses a DST boundary (see Risks).
series = [{"t": "2026-06-21T10:00:00Z", "v": 132},   # 11:00 Europe/London
          {"t": "2026-06-21T11:00:00Z", "v": 145},   # 12:00 Europe/London
          {"t": "2026-06-21T12:00:00Z", "v": 920}]   # 13:00 Europe/London  + 28d history
ctx = {"metric": "visitors", "timezone": "Europe/London", "period_hours": 168, "min_history_days": 7}
```

**Output Example:**
```python
{"metric": "visitors",
 "point_results": [
   {"detector": "robust_z", "p_value": 8.0e-12, "score": 6.9, "expected": 138, "ci": [95, 190]},
   {"detector": "seasonal_interval", "p_value": 3.1e-9, "expected": 138, "ci": [95, 190]},
   {"detector": "iforest", "p_value": 2.0e-7, "score": 0.71}],
 "as_of": "2026-06-21T12:00:00Z"}
```

**Acceptance Criteria:**
- On the running example, robust-z `p_value < 1e-6` and `expected ∈ [120,160]`.
- Returns valid p-values in `(0,1]`; no NaNs; handles < `min_history_days` by gating IsolationForest (cold start §5.1).
- Vectorized; ≤ 15 ms for 700 points (benchmarked in tests).
- **DST-safe:** series are normalized to a continuous, gap-free **UTC** hourly index before STL; a baseline window containing a DST transition (e.g. UK clocks change 2026-03-29 / 2026-10-25) must not raise a pandas frequency/index error — covered by an explicit spring-forward + fall-back test case.

**Key Code Snippet:**
```python
# detectors/point.py
import numpy as np
import pandas as pd
from statsmodels.tsa.seasonal import STL
from scipy.stats import norm
from sklearn.ensemble import IsolationForest

def to_regular_utc(series, freq="1h"):
    """series: [{'t': ISO-8601, 'v': float}]. STL requires a continuous, gap-free,
    evenly-spaced index. Parsing local-offset strings that change across a DST
    boundary would make the index irregular and raise on STL. Fix: parse EVERY
    point to UTC (utc=True coerces mixed offsets), then reindex onto a gap-free
    UTC date_range so DST shifts and missing buckets cannot break the frequency."""
    idx = pd.to_datetime([p["t"] for p in series], utc=True)
    s = pd.Series([p["v"] for p in series], index=idx).sort_index()
    full = pd.date_range(s.index.min(), s.index.max(), freq=freq, tz="UTC")
    return s.reindex(full).interpolate(limit_direction="both")

def fit_robust_z(series, period):
    """NIGHTLY fit (batch). Runs STL over the 28-90d MV history and stores a
    compact baseline. This is the ONLY place STL runs — never on the hot path."""
    s = to_regular_utc(series)
    res = STL(s.to_numpy(), period=period, robust=True).fit().resid
    med = float(np.median(res))
    mad = 1.4826 * float(np.median(np.abs(res - med))) or 1e-9
    seasonal = seasonal_profile_by_hour_of_week(s, res)   # expected value per hour-of-week
    return {"median": med, "mad": mad, "seasonal": seasonal}            # persisted in the model

def score_robust_z(baseline, t_utc, observed):
    """HOT path (per scan). O(1): no STL — just residual vs the stored baseline."""
    resid = observed - baseline["seasonal"][hour_of_week(t_utc)]
    z = (resid - baseline["median"]) / baseline["mad"]
    return {"detector": "robust_z", "score": float(z), "p_value": float(norm.sf(abs(z)))}

def iforest(feature_matrix):
    clf = IsolationForest(n_estimators=200, contamination="auto", random_state=0).fit(feature_matrix[:-1])
    # score_samples returns the NEGATED anomaly score (lower/more-negative = more
    # anomalous). We negate so higher s = more anomalous. NOTE: this scale is
    # relative to THIS fitted forest, so it shifts on every retrain — the sign
    # convention + the calibration quantile must be re-derived and stored per
    # model version in the registry (T2.4) to avoid score flipping across drift.
    s = -clf.score_samples(feature_matrix[-1:])[0]
    p = float(np.clip(np.exp(-s), 1e-12, 1.0))
    return {"detector": "iforest", "score": float(s), "p_value": p}
```

**Required Skills:** Python; NumPy; statsmodels STL; scikit-learn Isolation Forest; robust statistics (MAD).

**Required Knowledge:** Seasonal-trend decomposition; one-sided p-values; contextual vs point anomalies; **pandas tz-aware datetime handling** (`utc=True` parsing, `date_range`, `reindex`) and why STL needs an evenly-spaced index; the Isolation Forest `score_samples` sign convention.

**Self-Learning References:** statsmodels STL (`https://www.statsmodels.org/stable/generated/statsmodels.tsa.seasonal.STL.html`); Isolation Forest + `score_samples` (`https://scikit-learn.org/stable/modules/generated/sklearn.ensemble.IsolationForest.html`); MAD robust z (`https://en.wikipedia.org/wiki/Median_absolute_deviation`); pandas time-zone handling & DST (`https://pandas.pydata.org/docs/user_guide/timeseries.html#time-zone-handling`).

**Estimated Effort:** 5 days.

**Risks:**
- **DST / timezone index mismatch (high):** the Elixir worker (T3.2) must emit series timestamps as **UTC instants**, and this module must parse them with `utc=True` and reindex onto a gap-free UTC `date_range` *before* STL. If local-offset strings (e.g. `…+01:00` then `…+00:00` after the UK Oct clock change) reach STL, pandas raises a frequency/irregular-index error and the whole site's scan fails. Mitigation: `to_regular_utc()` above + the DST acceptance test; contract-level fix in T3.2/§6.1.
- **IsolationForest score scale not portable across versions (medium):** `score_samples` is relative to the fitted forest; absolute scores (and therefore severity) shift on retrain. Mitigation: re-derive and persist the calibration quantile + sign convention **per model version** in the registry (T2.4), and interpret a score only against its own version's baseline distribution — never compare raw scores across versions during a concept-drift update (§5.2).
- STL instability on short/sparse series → mitigate by gating (cold start §5.1) and falling back to seasonal-naïve when history < period.

---

### Task T1.2 — Composition / mix-shift detector (`detectors/composition.py`)

**Objective:** Implement chi-square/G-test mix-shift, new-entity Poisson-tail, and referrer-spam detection across `visit:source/country/device/browser`, emitting per-dimension p-values and the offending entity.

**Dependencies:** T0.1. Mockable.

**Input Example:**
```python
distributions = {
  "visit:browser": {"baseline": {"Chrome":0.62,"Safari":0.30,"Headless Chrome":0.01,"Firefox":0.07},
                    "current":  {"Chrome":0.40,"Safari":0.18,"Headless Chrome":0.38,"Firefox":0.04}},
  "visit:source":  {"baseline": {"Google":0.55,"Direct":0.30,"Twitter":0.15},
                    "current":  {"Google":0.20,"Direct":0.10,"buy-cheap-followers.ru":0.70}}}
totals = {"baseline_n": 19500, "current_n": 1180}
```

**Output Example:**
```python
{"composition_results": [
  {"dimension":"visit:browser","kind":"composition","entity":"Headless Chrome",
   "baseline_share":0.01,"current_share":0.38,"p_value":4.0e-10},
  {"dimension":"visit:source","kind":"new_entity","entity":"buy-cheap-followers.ru",
   "current_share":0.70,"p_value":2.0e-13,"spam_listed":true}]}
```

**Acceptance Criteria:**
- Headless-Chrome shift yields `p_value < 1e-6` at *flat total volume*.
- New, unseen `visit:source` with high share flagged via Poisson tail; known referral-spam entities marked `spam_listed=true`.
- Chi-square uses counts (not just shares); guards against zero expected cells (add-α smoothing).

**Key Code Snippet:**
```python
# detectors/composition.py
import numpy as np
from scipy.stats import chi2_contingency, poisson

def mix_shift(baseline, current, n_base, n_cur, alpha=0.5):
    keys = sorted(set(baseline) | set(current))
    b = np.array([baseline.get(k,0)*n_base + alpha for k in keys])
    c = np.array([current.get(k,0)*n_cur + alpha for k in keys])
    _, p, _, _ = chi2_contingency(np.vstack([b, c]))
    worst = keys[int(np.argmax(np.abs(c/c.sum() - b/b.sum())))]
    return {"dimension": None, "kind": "composition", "entity": worst, "p_value": float(p)}

def new_entity_poisson(expected_rate, observed_count):
    return {"kind": "new_entity", "p_value": float(poisson.sf(observed_count - 1, expected_rate))}
```

**Required Skills:** Python; scipy.stats; categorical hypothesis testing; Poisson modeling.

**Required Knowledge:** Chi-square vs G-test; multiple-comparison awareness; referral-spam patterns; Plausible dimensions.

**Self-Learning References:** chi2_contingency (`https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.chi2_contingency.html`); G-test (`https://en.wikipedia.org/wiki/G-test`); Poisson tail (`https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.poisson.html`); referral spam background (`https://plausible.io/docs/excluding`).

**Estimated Effort:** 5 days.

**Risks:** High-cardinality dimensions (many sources) inflate false positives → mitigate by top-k truncation + "other" bucket + per-dimension calibration.

---

### Task T1.3 — Joint scoring + adaptive calibration (`scoring.py`, `calibration.py`)

**Objective:** Fuse all detector p-values via weighted Stouffer Z with dependence correction, and set per-`(site, metric)` thresholds from the empirical-quantile alert budget, modulated by feedback.

**Dependencies:** T1.1, T1.2 (consumes their outputs). Mockable.

**Input Example:**
```python
p_values = [8.0e-12, 3.1e-9, 2.0e-7, 4.0e-10]    # robust_z, seasonal, iforest, composition
weights  = [1.0, 0.8, 0.6, 0.9]
rho_hat  = 0.35
baseline_scores = [...]            # historical joint Z's for (4242, visitors)
budget = {"target_alerts_per_week": 1.0, "points_per_week": 168, "feedback_inflation": 1.0}
```

**Output Example:**
```python
{"joint": {"Z": 7.9, "p_joint": 1.4e-15},
 "threshold": 4.6, "alert": true, "severity": "high"}
```

**Acceptance Criteria:**
- `rho_hat=0` reduces to standard weighted Stouffer; `rho_hat>0` lowers `Z` (verified by unit test).
- Threshold reproduces the target alert rate ±20% on a backtest stream.
- `feedback_inflation > 1` raises the threshold for suppressed signatures.

**Key Code Snippet:** *(see §4.5 `joint_score` and §4.6 `calibrated_threshold` — both implemented here; severity buckets from Z: ≥6 high, ≥4.5 medium, ≥ threshold low).*
```python
def severity(Z, thr):
    if Z >= 6: return "high"
    if Z >= 4.5: return "medium"
    return "low" if Z >= thr else None
```

**Required Skills:** Statistical meta-analysis (Stouffer/Fisher); quantile calibration; Python/NumPy/scipy.

**Required Knowledge:** Dependence correction for correlated tests; alert-budget framing; the §4.5/§4.6 design.

**Self-Learning References:** Stouffer's method (`https://en.wikipedia.org/wiki/Fisher%27s_method#Relation_to_Stouffer's_Z-score_method`); combining dependent p-values (Brown's method) (`https://en.wikipedia.org/wiki/Fisher%27s_method`); quantiles (`https://numpy.org/doc/stable/reference/generated/numpy.quantile.html`).

**Estimated Effort:** 4 days.

**Risks:** Mis-estimated `rho_hat` skews fusion → mitigate by bounding `rho_hat ∈ [0, 0.8]` and back-testing on labelled data (T4.2).

---

### Task T2.1 — FastAPI service skeleton + auth + schemas (`app/main.py`, `auth.py`, `schemas.py`, `config.py`)

**Objective:** Stand up the ADS service implementing the frozen contract, wiring service-token auth, pydantic validation, config, and `/healthz` + `/metrics`, with detectors injected behind interfaces.

**Dependencies:** T0.1. (Real detectors land via T1.*/T2.* but skeleton uses the mock initially.)

**Input Example:** `POST /v1/detect` with `Authorization: Bearer plausible-ads-prod_…` and the Part-6.1 body.

**Output Example:** the Part-6.1 response (200); `401` if token missing/invalid; `413` if body > 2 MB.

**Acceptance Criteria:**
- Valid token → 200; missing/invalid → 401; oversized body → 413.
- pydantic rejects malformed payloads with 422 + field errors.
- `/healthz` returns models-loaded count; `/metrics` exposes Prometheus counters.
- Runs under `uvicorn` with ≥ 4 workers; graceful shutdown.

**Key Code Snippet:**
```python
# app/auth.py
import hmac, hashlib
from fastapi import Header, HTTPException
def verify(token_hash_env: str, authorization: str = Header(default="")):
    if not authorization.startswith("Bearer "):
        raise HTTPException(401, "missing bearer")
    raw = authorization.removeprefix("Bearer ").strip()
    digest = hashlib.sha256(raw.encode()).hexdigest()
    if not hmac.compare_digest(digest, token_hash_env):
        raise HTTPException(401, "invalid token")
```

**Required Skills:** FastAPI; pydantic; uvicorn; dependency injection; prometheus-client.

**Required Knowledge:** Plausible's auth pattern (`lib/plausible_web/plugs/authorize_public_api.ex` — Bearer + SHA-256); 12-factor config.

**Self-Learning References:** FastAPI security (`https://fastapi.tiangolo.com/tutorial/security/`); pydantic (`https://docs.pydantic.dev/`); prometheus-client (`https://github.com/prometheus/client_python`); uvicorn (`https://www.uvicorn.org/`).

**Estimated Effort:** 4 days.

**Risks:** Constant-time token comparison must be used (timing attack) → enforced via `hmac.compare_digest` and covered in T4.3.

---

### Task T2.2 — Privacy data-leak detector (`detectors/privacy.py`)

**Objective:** Implement the leak detector (regex + Shannon entropy + Luhn) and the re-identification-risk detector (3b), with **redaction in memory before any storage or logging**.

**Dependencies:** T0.1, T2.1 (endpoint). Stateless — no history needed.

**Input Example:** the Part-6.3 `/v1/privacy_scan` request (running example `Signup` props).

**Output Example:** the Part-6.3 response — `email` (`j***@***.com`) and `pan_luhn16` (`4111********1111`), `path` clean.

**Acceptance Criteria:**
- `jane.doe@gmail.com` → `type=email`; `4111111111111111` → `type=pan_luhn16, confidence=1.0`; `/checkout` → clean.
- Raw values never appear in logs or the response; structured logger scrubs by regex.
- Re-id detector flags a custom prop that is unique-per-visitor **and** stable across salt windows.

**Key Code Snippet:**
```python
# detectors/privacy.py
import math, re
EMAIL = re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+")
def luhn_ok(s):
    d = [int(c) for c in s if c.isdigit()]
    if len(d) < 13: return False
    chk = d[-1]; body = d[-2::-1]
    tot = sum((x*2 - 9 if x*2 > 9 else x*2) if i % 2 == 0 else x for i, x in enumerate(body))
    return (tot + chk) % 10 == 0
def shannon(s):
    if not s: return 0.0
    p = [s.count(c)/len(s) for c in set(s)]
    return -sum(pi*math.log2(pi) for pi in p)
def scan(value):
    if EMAIL.search(value): return ("email", 0.99)
    if luhn_ok(value):       return ("pan_luhn16", 1.0)
    if shannon(value) > 3.5 and len(value) >= 20: return ("high_entropy_secret", 0.7)
    return (None, 0.0)
```

**Required Skills:** Python regex; entropy; Luhn; secure logging/redaction.

**Required Knowledge:** PII/PCI categories; Plausible props storage (`meta.key`/`meta.value` arrays in `events_v2`, `lib/plausible/props.ex`); the salt model for the re-id check (`lib/plausible/session/salts.ex`).

**Self-Learning References:** Luhn (`https://en.wikipedia.org/wiki/Luhn_algorithm`); Shannon entropy (`https://en.wikipedia.org/wiki/Entropy_(information_theory)`); Microsoft Presidio (optional) (`https://microsoft.github.io/presidio/`); OWASP logging cheat sheet (`https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html`).

**Estimated Effort:** 5 days.

**Risks:** Over-redaction (false positives on legit high-entropy ids) → mitigate with confidence scores + allowlist of known-safe internal keys (`url`, `path`, `search_query` per `props.ex`).

---

### Task T2.3 — Returning-visitor population estimator (`detectors/returning.py`)

**Objective:** Estimate population multi-day repeat rate from 48h-salt-bounded observations, extrapolate via BG/NBD, enforce a k-anonymity floor — never tracking individuals.

**Dependencies:** T0.1, T2.1. Needs aggregated counts only (from ECE via worker).

**Input Example:** the Part-6.4 `/v1/returning_visitors` request (running example counts).

**Output Example:** the Part-6.4 response (`observed=0.27`, `estimated_30d=0.34`, CI `[0.30,0.38]`, `k_anonymity_ok=true`).

**Acceptance Criteria:**
- Output suppressed (`k_anonymity_ok=false`, rates `null`) when any cohort < `k`.
- BG/NBD parameters fit on the observed window; CI via bootstrap.
- No `user_id` or per-person record is read, stored, or returned (verified in T4.3).

**Key Code Snippet:**
```python
# detectors/returning.py
def estimate(users_1d, users_2plus_48h, k_floor=50, horizon_days=30):
    n = users_1d + users_2plus_48h
    if min(users_1d, users_2plus_48h) < k_floor:
        return {"k_anonymity_ok": False, "observed_multi_day_rate": None,
                "estimated_30d_repeat_rate": None}
    observed = users_2plus_48h / n
    # BG/NBD-style extrapolation factor from 48h window to horizon (fitted offline; illustrative):
    extrap = 1.0 + 0.26 * (horizon_days / 2 - 1) ** 0.5
    est = min(0.95, observed * extrap)
    return {"k_anonymity_ok": True, "observed_multi_day_rate": round(observed, 2),
            "estimated_30d_repeat_rate": round(est, 2)}
```

**Required Skills:** Python; `lifetimes` (BG/NBD); bootstrap CIs; privacy-preserving aggregation.

**Required Knowledge:** BG/NBD model; k-anonymity; **critically**, Plausible's 48h salt deletion (`lib/plausible/session/salts.ex`) and why cross-day individual tracking is impossible (§1.4).

**Self-Learning References:** BG/NBD paper (`https://www.brucehardie.com/papers/018/`); `lifetimes` lib (`https://lifetimes.readthedocs.io/`); k-anonymity (`https://en.wikipedia.org/wiki/K-anonymity`); Plausible data policy (`https://plausible.io/data-policy`).

**Estimated Effort:** 6 days.

**Risks:** Over-claiming precision could mislead users / raise privacy concerns → mitigate by always shipping the CI + the "population estimate, not tracking" caveat in the response and the UI.

---

### Task T2.4 — Training jobs, model registry, feature-store reader, drift/retraining (`training.py`, `registry.py`, `feature_store.py`)

**Objective:** Implement nightly per-site refit + backtest, S3+Postgres model registry, MV reader (read-only CH), drift detection (Page-Hinkley/ADWIN), and `promote_to` (shadow/candidate/active).

**Dependencies:** T0.2 (registry table), T0.3 (MV + CH user), T1.* (models to train), T2.1 (`/v1/train`).

**Input Example:** the Part-6.2 `/v1/train` request (`site_ids=[4242]`, window `2026-05-01..2026-06-20`, `promote_to=shadow`).

**Output Example:** the Part-6.2 response (`v8`, `artifact_s3_key`, backtest precision/recall, `status=shadow`).

**Acceptance Criteria:**
- Trains all requested kinds; writes artifact to S3 and a row to `ads_model_registry` with `feature_schema_version`.
- **Artifact embeds the compact baseline** that powers O(K) hot-path scoring with **no history read**: per-metric seasonal-by-hour-of-week profile, residual median + MAD, the fitted IsolationForest, the composition baseline distributions, and the calibration quantile/threshold. The detect path (T1.1 `score_*`, §6.1) reads only this — the 28–90d series is consumed here at fit time, not at scan time (ADR-2).
- Backtest precision/recall computed on a held-out window; stored in `metrics` jsonb.
- Drift on the residual stream triggers rebase + recalibrate (§5.2); model resolution honours `status` for instant rollback (§5.7).
- **Score scale pinned per version (anti-flip):** each artifact is persisted together with its IsolationForest `score_samples` sign convention and the calibration quantile/threshold derived from *that* forest's score distribution (§4.6). A finding's score is only ever interpreted against its own model version's baseline — raw scores are never compared across versions. A retrain/drift update that would shift the score profile must recompute and store a fresh threshold atomically with the new artifact, so a version swap cannot flip severity. Covered by a regression test that re-scores a fixed sample across two consecutive versions and asserts severity stability.

**Key Code Snippet:**
```python
# registry.py
import joblib, io, boto3
def save_artifact(s3, bucket, key, model):
    buf = io.BytesIO(); joblib.dump(model, buf); buf.seek(0)
    s3.put_object(Bucket=bucket, Key=key, Body=buf.read())

def resolve_active(pg, site_id, kind):           # instant rollback = flip status rows
    row = pg.fetchone(
        "SELECT artifact_s3_key, version FROM ads_model_registry "
        "WHERE site_id=%s AND model_kind=%s AND status='active' "
        "ORDER BY trained_at DESC LIMIT 1", (site_id, kind))
    return row
```

**Required Skills:** scikit-learn/statsmodels persistence (joblib); boto3/S3; ClickHouse client; drift detection; backtesting.

**Required Knowledge:** Model registry patterns; point-in-time feature correctness; Plausible S3 setup (`lib/plausible/s3.ex`); the MV schema (T0.3).

**Self-Learning References:** joblib persistence (`https://scikit-learn.org/stable/model_persistence.html`); ADWIN/Page-Hinkley via `river` (`https://riverml.xyz/latest/api/drift/ADWIN/`); clickhouse-connect (`https://clickhouse.com/docs/en/integrations/python`); MLOps registry concepts (`https://ml-ops.org/content/mlops-principles`).

**Estimated Effort:** 8 days.

**Risks:** Nightly fleet training cost at 1M sites (§7) → mitigate with sharded batch fleet, incremental refit, and skipping unchanged sites (no new data → no refit).

---

### Task T2.5 — Feedback→suppression + explanation generator (`app/main.py` feedback route, `explain.py`)

**Objective:** Implement `/v1/feedback` (immediate threshold inflation per signature) and templated human-readable explanations attached to findings.

**Dependencies:** T0.2 (suppression rule shape), T1.3 (calibration), T2.1.

**Input Example:** the Part-6.5 `/v1/feedback` request (`verdict=not_anomaly`, signature `v:visitors|k:point|sev:high`).

**Output Example:** the Part-6.5 response (`suppression_applied`, `threshold_inflation=1.6`).

**Acceptance Criteria:**
- A `not_anomaly` verdict immediately raises `feedback_inflation` for the signature so the next matching score is muted (verified by a before/after detect call).
- Explanations are deterministic, PII-free, and reference the running-example numbers (observed/expected/driver share).
- `match_all` and `signature` scopes supported; effect time-boxed by `effective_until`.

**Key Code Snippet:**
```python
# explain.py
TEMPLATE = ("Visitors were {ratio:.1f}x the expected {expected}. "
            "{pct}% of the surge came from '{entity}' ({dimension}) — likely {cause}.")

def explain_point(observed, expected, top):
    ratio = observed / max(expected, 1)
    cause = "referral spam" if top["dimension"] == "visit:source" else "a bot signature"
    return TEMPLATE.format(
        ratio=ratio,
        expected=expected,
        pct=int(top["contribution"] * 100),
        entity=top["entity"],
        dimension=top["dimension"],
        cause=cause,
    )
```

**Required Skills:** FastAPI; templating; state management for suppression.

**Required Knowledge:** The signature scheme (detection-time coarse + notification-time driver); calibration modulation (§4.6); §5.7 rollback semantics.

**Self-Learning References:** FastAPI request bodies (`https://fastapi.tiangolo.com/tutorial/body/`); idempotency keys (`https://stripe.com/docs/api/idempotent_requests`); human-centered alerting (`https://sre.google/sre-book/monitoring-distributed-systems/`).

**Estimated Effort:** 4 days.

**Risks:** Inflation that never decays permanently blinds a site → mitigate with `effective_until` expiry + periodic decay of `feedback_inflation` toward 1.0.

---

### Task T3.1 — ADS HTTP client (`lib/plausible/anomaly_detection/client.ex`)

**Objective:** Implement the typed Elixir client wrapping `Plausible.HTTPClient` for all ADS `/v1` calls, with timeouts, retries, and a circuit-breaker fallback to legacy alerting.

**Dependencies:** T0.1 (contract). Can build against the T0.1 mock immediately.

**Input Example:**
Elixir call:
```elixir
Plausible.AnomalyDetection.Client.detect(%{
  site_id: 4242, site_domain: "acme-shop.example", timezone: "Europe/London",
  as_of: ~U[2026-06-21 13:00:00Z],
  metrics: %{"visitors" => %{granularity: "1h", series: [
    %{t: "2026-06-21T12:00:00Z", v: 920}], baseline_ref: "28d"}},
  alert_budget_per_week: 1.0
})
```

**Output Example:**
```elixir
{:ok, %{
  "anomalies" => [%{"finding_id" => "fnd_2026-06-21_4242_0007", "kind" => "point",
    "metric" => "visitors", "observed" => 920, "expected" => 138, "severity" => "high"}],
  "model" => %{"point" => "v7", "composition" => "v3"}}}
```

**Acceptance Criteria:**
- Sends Bearer token; gzip-encodes bodies > 16 KB; 2s connect / 10s read timeout.
- Maps non-200 to `{:error, reason}`; opens a circuit breaker after N consecutive failures → caller falls back to legacy path.
- Unit-tested against the T0.1 mock and a stubbed `Plausible.HTTPClient`.

**Key Code Snippet:**
```elixir
# lib/plausible/anomaly_detection/client.ex
defmodule Plausible.AnomalyDetection.Client do
  @base Application.compile_env(:plausible, [:anomaly_detection, :base_url])
  def detect(payload) do
    headers = [{"authorization", "Bearer " <> token()}, {"content-type", "application/json"}]
    case Plausible.HTTPClient.post("#{@base}/v1/detect", headers, payload) do
      {:ok, %{body: body}} -> {:ok, body}
      {:error, e} -> {:error, e}
    end
  end
  defp token, do: Application.get_env(:plausible, :anomaly_detection)[:token]
end
```

**Required Skills:** Elixir; `Plausible.HTTPClient`/Finch; error handling; circuit breakers.

**Required Knowledge:** Plausible HTTP client conventions (`lib/plausible/http_client.ex`); config via `Application.get_env`; the ADS contract.

**Self-Learning References:** Finch (`https://hexdocs.pm/finch/`); `Plausible.HTTPClient` (repo `lib/plausible/http_client.ex`); circuit breaker pattern (`https://martinfowler.com/bliki/CircuitBreaker.html`).

**Estimated Effort:** 3 days.

**Risks:** ADS latency stalling the worker → mitigate with tight timeouts + circuit breaker + legacy fallback (Part 7, Stage 3).

---

### Task T3.2 — `AnomalyScan` Oban worker + cron + context (`lib/workers/anomaly_scan.ex`, `lib/plausible/anomaly_detection.ex`)

**Objective:** Implement the scheduled worker that, per enabled site shard, sends a **thin scan trigger** to ADS (recent points + current distribution snapshot only — *no* 28–90d history; ADR-2) and persists findings — modeled on `TrafficChangeNotifier` but keeping the BEAM hot path allocation-light at 100K+ sites.

**Dependencies:** T0.2 (Finding schema), T0.3 (MV — ADS reads baseline from it), T3.1 (client). Integrates with real ADS in Phase 2.

**Input Example:**
The thin payload the worker assembles for `site_id=4242`. It contains only the last K buckets (flush-lag freshness) and the current distribution snapshot — **not** a long series. `recent_points[].t` are UTC instants (`DateTime.to_iso8601/1` → `…Z`), never local-offset strings; `timezone` is carried once (T1.1/§6.1 DST contract):
```elixir
%{site_id: 4242, site_domain: "acme-shop.example", timezone: "Europe/London",
  as_of: "2026-06-21T13:00:00Z",
  metrics: %{"visitors" => %{granularity: "1h", recent_points: [
    %{t: "2026-06-21T11:00:00Z", v: 145},   # ≤ 6 points, NOT 28-90 days
    %{t: "2026-06-21T12:00:00Z", v: 920}
  ]}},
  distributions_current: dists_now_4242,     # current-window shares only; baseline lives in the model
  alert_budget_per_week: 1.0}
```

**Output Example:** Persisted side effect — one `ads_findings` row per anomaly (the T0.2 Output Example map) + a `notified_at` timestamp on high-severity findings.

**Acceptance Criteria:**
- Cron-registered (`config/runtime.exs`, beside `TrafficChangeNotifier`); cadence configurable (hourly default; can run every 15 min because the hot path is O(K) per site).
- **Burst-flattening + tail isolation (§7.3):** a dispatcher **drips** scans across the whole interval (deterministic `site_id`-keyed minute-bucketing + token-bucket enqueue) rather than enqueuing the whole shard at `:00` — bounding instantaneous queue depth so ADS sees steady RPS. Tier-L (whale) sites route to a separate `:anomaly_scan_whales` queue so a few huge sites can't head-of-line-block the long tail. A test asserts enqueue rate stays under the configured ceiling (no thundering herd).
- Skips sites where `FunWithFlags.enabled?(:anomaly_detection, for: site)` is false or a `match_all` suppression exists.
- **Hot path is allocation-light:** builds only the trailing ≤ K buckets + current distribution snapshot (small `Plausible.Stats` query against the MV, not raw `events_v2`); the per-scan payload is ≤ ~2 KB and contains **no** multi-week series. A benchmark/regression asserts payload size and that BEAM does no large-array (28–90d) serialization on the hot path.
- **Serializes every `recent_points[].t` as a UTC instant** (`…Z`), not a site-local offset; a unit test asserts no `+HH:MM`/`-HH:MM` offsets appear in any payload timestamp (guards the T1.1 STL index against DST breakage).
- `sign`-correct session metrics (handled by the MV at build time, T0.3); persists findings idempotently (unique `finding_id`).

**Key Code Snippet:**
```elixir
# lib/workers/anomaly_scan.ex  — thin trigger; baseline read by ADS from the MV/model
defmodule Plausible.Workers.AnomalyScan do
  use Oban.Worker, queue: :anomaly_scan
  alias Plausible.AnomalyDetection
  @impl true
  def perform(%Oban.Job{args: %{"shard" => shard, "shards" => n}}) do
    AnomalyDetection.sites_for_shard(shard, n)
    |> Stream.filter(&FunWithFlags.enabled?(:anomaly_detection, for: &1))
    |> Stream.reject(&AnomalyDetection.match_all_suppressed?/1)
    |> Enum.each(fn site ->
      # small query: only the trailing K buckets + current distribution snapshot
      payload = AnomalyDetection.build_scan_trigger(site, recent_buckets: 6)
      with {:ok, %{"anomalies" => anomalies}} <- AnomalyDetection.Client.detect(payload) do
        AnomalyDetection.persist_and_notify(site, anomalies)
      end
    end)
    :ok
  end
end
```

**Required Skills:** Elixir; Oban workers & cron; `Plausible.Stats` query engine; Ecto writes; BEAM memory/GC awareness (avoiding large transient binaries on the hot path).

**Required Knowledge:** The `TrafficChangeNotifier` worker pattern (`lib/workers/traffic_change_notifier.ex`); Oban cron registration (`config/runtime.exs:824`); the Stats query API (`lib/plausible/stats/query.ex`); FunWithFlags actors (`lib/plausible/site.ex:246`); why the baseline must NOT be pushed (ADR-2).

**Self-Learning References:** Oban (`https://hexdocs.pm/oban/Oban.html`); Oban cron (`https://hexdocs.pm/oban/Oban.Plugins.Cron.html`); FunWithFlags (`https://hexdocs.pm/fun_with_flags/`); Erlang/BEAM binaries & GC (`https://www.erlang.org/doc/efficiency_guide/binaryhandling.html`); repo `lib/workers/traffic_change_notifier.ex`.

**Estimated Effort:** 7 days.

**Risks:**
- **BEAM GC / CPU pressure from large payloads (high) — the defect this design avoids:** serializing 28–90d hourly JSON per site per scan would, at 100K+ sites under 15-min scans, spike BEAM allocation/GC and CPU on the hot path. Mitigation: the thin-trigger contract (ADR-2) — baseline lives in the model/MV; payload ≤ ~2 KB; size-bounded benchmark in CI.
- **Local-offset timestamp leakage (high):** if `recent_points[].t` are serialized in local time, a window crossing a DST change feeds STL a non-monotonic index and the scan crashes (see T1.1). Mitigation: UTC-only serialization + the offset-free unit test above.
- All shards firing at `:00` overloads ADS → mitigate by minute-offset per shard (cron `7 * * * *` + in-worker jitter) (ADR-8).

---

### Task T3.3 — Notifications + feedback ingestion + suppression apply (`email.ex`, context)

**Objective:** Persist findings to Postgres as the **single transactional source of truth** — the `ads_findings` row + its outbox marker (`ch_synced_at = NULL`) written in **one transaction** (ADR-10; ClickHouse propagation is the async T3.6 worker, *not* a synchronous dual-write). Compose & deliver anomaly e-mails (reusing the mailer); expose the authenticated feedback action that updates status, writes `ads_suppression_rules`, re-arms the outbox (so the status change re-projects to CH), and calls `/v1/feedback`; apply suppression at notify time.

**Dependencies:** T0.2 (PG schema + outbox column), T3.1 (client), T3.2 (findings). Pairs with T3.6 (CH sync). (No longer writes CH directly.)

**Input Example:**
Feedback action params:
```elixir
%{"site_id" => 4242, "finding_id" => "fnd_2026-06-21_4242_0007",
  "verdict" => "not_anomaly", "scope" => "signature"}
```

**Output Example:**
E-mail subject + a suppression row:
```text
Subject: "Unusual traffic on acme-shop.example — 6.7x expected, likely referral spam"
=> inserts %Plausible.AnomalyDetection.SuppressionRule{site_id: 4242, scope: :signature,
     detection_signature: "v:visitors|k:point|sev:high", threshold_inflation: 1.6}
```

**Acceptance Criteria:**
- **Transactional outbox, not dual-write (ADR-10):** the `ads_findings` row and its `ch_synced_at = NULL` marker are written in a single Postgres transaction; ADS never writes ClickHouse synchronously here. A feedback/status change updates the row **and** resets `ch_synced_at = NULL` so T3.6 re-projects it. A test asserts that if ClickHouse is down, the PG write still commits and the row is later reconciled (no drift).
- `anomaly_notification/4` renders the running-example explanation; respects suppression (no e-mail if signature suppressed).
- Feedback action enforced by `AuthorizeSiteAccess`; rate-limited via `Plausible.RateLimit`.
- Writes suppression row AND calls `/v1/feedback`; both must succeed or the action reports partial failure.

**Key Code Snippet:**
```elixir
# lib/plausible_web/email.ex  (new function, mirrors spike_notification/4)
def anomaly_notification(email, site, finding, dashboard_link) do
  base_email()
  |> to(email)
  |> tag("anomaly-notification")
  |> subject("Unusual traffic on #{site.domain} — #{finding.explanation_headline}")
  |> render("anomaly_notification.html",
       %{site: site, finding: finding, link: dashboard_link})
end
```

**Required Skills:** Elixir; Bamboo/`Plausible.Mailer`; Phoenix controllers/plugs; rate limiting.

**Required Knowledge:** `PlausibleWeb.Email` + templates pattern (`spike_notification.html.heex`); `AuthorizeSiteAccess` plug; `Plausible.RateLimit` usage.

**Self-Learning References:** Bamboo (`https://hexdocs.pm/bamboo/`); Phoenix controllers (`https://hexdocs.pm/phoenix/controllers.html`); repo `lib/plausible_web/email.ex`; repo `lib/plausible/rate_limit.ex`.

**Estimated Effort:** 5 days.

**Risks:** Two-system write (Postgres + ADS) partial failure → mitigate by writing the suppression row first (source of truth) and treating `/v1/feedback` as best-effort with retry.

---

### Task T3.4 — Feature-flag gating + settings toggle (server-rendered HEEx) + internal findings/feedback JSON API

> **Architecture note (corrected):** Plausible's site *settings* pages are **server-rendered Phoenix HEEx dead-views** (e.g. `lib/plausible_web/templates/site/settings_email_reports.html.heex`, rendered by `PlausibleWeb.SiteController.settings_email_reports/2` with form POSTs that `redirect` back) — **not** LiveView and **not** React. The existing spike/drop toggle lives there, so the "Anomaly alerts" toggle is added there too. The React dashboard *insights* panel is a separate task (T3.5). This card owns the Elixir/HEEx surface only.

**Objective:** Gate the feature behind `:anomaly_detection`; add the "Anomaly alerts" enable/disable + recipients + budget controls to the server-rendered Email-reports settings page (mirroring `enable_traffic_change_notification`); and expose the internal JSON endpoints the React dashboard (T3.5) calls to list findings and submit feedback.

**Dependencies:** T3.3 (feedback action + suppression), T0.2 (schemas). Blocks T3.5 (the SPA consumes these endpoints).

**Input Example:**
Router additions under the dashboard's existing internal pipeline (the SPA already calls `/api/stats/:domain/...` via `url.apiPath`):
```elixir
# lib/plausible_web/router.ex — inside  scope "/api" do pipe_through :internal_stats_api
#   scope "/stats", PlausibleWeb.Api do ...  (this scope already applies AuthorizeSiteAccess)
get  "/:domain/anomaly-findings",                 AnomalyController, :index
post "/:domain/anomaly-findings/:finding_id/feedback", AnomalyController, :feedback
# Settings form actions live on the dashboard (dead-view) router, beside the
# existing enable/disable_traffic_change_notification routes -> SiteController.
```

**Output Example:**
`GET /api/stats/acme-shop.example/anomaly-findings` JSON (consumed by the SPA in T3.5):
```json
{"findings": [
  {"finding_id": "fnd_2026-06-21_4242_0007", "kind": "point", "metric": "visitors",
   "severity": "high", "observed": 920, "expected": 138,
   "headline": "6.7x expected on Jun 21, 1pm — likely referral spam",
   "detection_signature": "v:visitors|k:point|sev:high", "feedback": null}]}
```
And the server-rendered settings fragment (HEEx output):
```text
Settings → Email reports → "Anomaly alerts"  [✓ Enabled]
  Alert budget: ~1 per week   Recipients: owner@acme-shop.example
```

**Acceptance Criteria:**
- Settings toggle (HEEx form → `SiteController` action) flips `FunWithFlags.enable/disable(:anomaly_detection, for: site)`; section hidden when flag off; styled like the existing spike/drop block in `settings_email_reports.html.heex`.
- `GET /anomaly-findings` runs a **single paginated ClickHouse query** against `ads_findings_history` and returns the rows straight through as JSON — **no Postgres↔ClickHouse join and no nested-map stitching in BEAM** (ADR-11). Status is read from the denormalized read model (kept fresh by T3.6); active suppression is applied as a bounded query filter. Work per request is bounded by page size (≤50), independent of total history size. `POST .../feedback` invokes the T3.3 feedback action; both run under `:internal_stats_api` (so `AuthorizeSiteAccess` is enforced) and return `403` for non-members.
- No LiveView is introduced; the settings change is a classic controller render + form POST + redirect, consistent with the surrounding code.

**Key Code Snippet:**
```elixir
# lib/plausible_web/controllers/api/anomaly_controller.ex  (PlausibleWeb.Api)
defmodule PlausibleWeb.Api.AnomalyController do
  use PlausibleWeb, :controller
  alias Plausible.AnomalyDetection
  def index(conn, params) do
    site = conn.assigns.site                       # set by AuthorizeSiteAccess
    # history from ClickHouse ads_findings_history (ADR-9), not the PG hot set
    json(conn, %{findings: AnomalyDetection.findings_history(site, params, limit: 50)})
  end
  def feedback(conn, %{"finding_id" => id} = params) do
    :ok = AnomalyDetection.apply_feedback(conn.assigns.site, id, params)
    json(conn, %{status: "ok"})
  end
end
```

**Required Skills:** Elixir; Phoenix controllers + **server-rendered HEEx dead-views** (not LiveView); FunWithFlags; routing/plugs; JSON APIs.

**Required Knowledge:** Where spike/drop settings actually render — `PlausibleWeb.SiteController.settings_email_reports/2` + `lib/plausible_web/templates/site/settings_email_reports.html.heex` + the `enable/disable/update_traffic_change_notification` actions; the dashboard's internal API scope (`scope "/api" → scope "/stats", PlausibleWeb.Api`, `pipe_through :internal_stats_api`, `lib/plausible_web/router.ex:272`); FunWithFlags actors (`lib/plausible/site.ex:246`).

**Self-Learning References:** Phoenix controllers & views (`https://hexdocs.pm/phoenix/controllers.html`); HEEx templates (`https://hexdocs.pm/phoenix_live_view/Phoenix.Component.html#sigil_H/2`); FunWithFlags (`https://hexdocs.pm/fun_with_flags/`); repo `lib/plausible_web/templates/site/settings_email_reports.html.heex`; repo `lib/plausible_web/router.ex`.

**Estimated Effort:** 5 days.

**Risks:** Mistaking the settings area for LiveView/React and over-engineering it → mitigate by copying the existing dead-view spike/drop pattern verbatim. Toggle exposed before backend ready → ship flag default-off and gate both the settings section and the API endpoints on the flag.

---

### Task T3.5 — Dashboard anomaly-insights panel + feedback (React / TypeScript SPA)

> **Architecture note (corrected):** Plausible's main analytics dashboard is a **React 18 / TypeScript single-page app** built by esbuild (entry `js/dashboard.tsx`, source under `assets/js/dashboard/`), using `@tanstack/react-query`, `react-router-dom`, `@headlessui/react`, and the shared fetch client `assets/js/dashboard/api.ts` (`url.apiPath(site, '/...')`). Anomaly *insights* (the findings feed + "Useful"/"Not an anomaly" buttons shown in-dashboard) belong here, following the existing `assets/js/dashboard/stats/<feature>/index.tsx` component pattern. This is the surface my earlier draft wrongly placed in HEEx/LiveView.

**Objective:** Build a React/TS dashboard panel that fetches the site's anomaly findings from the T3.4 internal API, renders them with explanations and severity, and lets the user submit "Useful"/"Not an anomaly" feedback (optimistic update via react-query), reusing existing dashboard components and styling.

**Dependencies:** T3.4 (the `/api/stats/:domain/anomaly-findings` + feedback endpoints). Mockable against the T0.1 contract / a JSON fixture until T3.4 lands.

**Input Example:**
The JSON returned by `GET /api/stats/acme-shop.example/anomaly-findings` (see T3.4 Output Example) — fetched via the existing client:
```typescript
// assets/js/dashboard/anomalies/fetch-anomalies.ts
import * as api from '../api'
import * as url from '../util/url'   // url.apiPath(site, path) -> /api/stats/:domain/...
export const fetchAnomalies = (site, query) =>
  api.get(url.apiPath(site, '/anomaly-findings'), query)
```

**Output Example:**
Rendered card in the dashboard (DOM, abbreviated) + the feedback request the button fires:
```text
[!] Unusual traffic — visitors 920 vs ~138 expected (Jun 21, 1pm)
    71% from buy-cheap-followers.ru on Headless Chrome · likely referral spam
    [ Useful ]   [ Not an anomaly ]
```
```http
POST /api/stats/acme-shop.example/anomaly-findings/fnd_2026-06-21_4242_0007/feedback
{ "verdict": "not_anomaly", "scope": "signature" }
```

**Acceptance Criteria:**
- New component tree under `assets/js/dashboard/anomalies/` (`index.tsx`, `anomaly-list.tsx`, `anomaly-card.tsx`, `feedback-buttons.tsx`, `fetch-anomalies.ts`) renders findings for the current site/date-range using the existing `site-context`/`query` contexts.
- Data fetched with `@tanstack/react-query`; feedback POST does an optimistic update + invalidates the findings query on success; failure rolls back and surfaces an error toast.
- Uses existing UI primitives (`@headlessui/react`, Tailwind classes) so it visually matches `stats/sources`, etc.; passes the repo's TypeScript/ESLint checks and `esbuild` bundle build.
- The panel only renders when the `:anomaly_detection` flag is on for the site (flag surfaced to the SPA via the existing bootstrap/site context).

**Key Code Snippet:**
```tsx
// assets/js/dashboard/anomalies/anomaly-list.tsx
import { useQuery } from '@tanstack/react-query'
import { fetchAnomalies } from './fetch-anomalies'
import { AnomalyCard } from './anomaly-card'

export function AnomalyList({ site, query }: { site: PlausibleSite; query: DashboardQuery }) {
  const { data, isLoading } = useQuery({
    queryKey: ['anomalies', site.domain, query],
    queryFn: () => fetchAnomalies(site, query)
  })
  if (isLoading) return <FadeIn>…</FadeIn>
  return <>{data!.findings.map((f) => <AnomalyCard key={f.finding_id} finding={f} site={site} />)}</>
}
```

**Required Skills:** React 18 + TypeScript; `@tanstack/react-query` (queries, mutations, optimistic updates); `react-router-dom`; Tailwind + `@headlessui/react`; the esbuild asset pipeline. **⚠ Skill-gap flag:** none of the four staffed engineers (2 Python SSE, 1 Elixir/ClickHouse, 1 QE) is a React/TS specialist — see Risks.

**Required Knowledge:** Dashboard SPA conventions — entry `js/dashboard.tsx`, `assets/js/dashboard/api.ts` (`url.apiPath`), the `stats/<feature>/index.tsx` pattern, `site-context.tsx`/`dashboard-state-context.tsx`, and how features are bundled by `esbuild` (`config/config.exs:19`, `mix.exs:213 assets.deploy`).

**Self-Learning References:** React docs (`https://react.dev/learn`); TanStack Query optimistic updates (`https://tanstack.com/query/latest/docs/framework/react/guides/optimistic-updates`); react-router (`https://reactrouter.com/en/main`); Headless UI React (`https://headlessui.com/react/menu`); esbuild (`https://esbuild.github.io/`); repo `assets/js/dashboard/stats/sources/index.tsx` as a worked example.

**Estimated Effort:** 6 days (assuming a frontend-capable engineer; +3–4 days of ramp if absorbed by a backend engineer).

**Risks:** **Primary risk is resourcing** — the staffed team has no dedicated frontend engineer, so this React/TS work is a skill gap. Mitigations, in preference order: (1) borrow a frontend engineer for this card; (2) assign to the most frontend-capable SSE and budget ramp time + the references above; (3) **ship v1 without the React panel** — the email path (T3.3) and the server-rendered settings page (T3.4) make the feature fully usable, so the in-dashboard insights panel can follow in a fast-follow release without blocking GA. This fallback keeps T3.5 off the critical path.

---

### Task T3.6 — Findings transactional outbox + reliable ClickHouse sync worker (`lib/workers/anomaly_findings_sync.ex`)

**Objective:** Implement the reliable, idempotent propagation that keeps ClickHouse `ads_findings_history` consistent with the Postgres source of truth **without a synchronous dual-write** (ADR-10). An Oban worker batch-reads un-synced `ads_findings` rows (`ch_synced_at IS NULL`), inserts them into `ads_findings_history` idempotently, and stamps `ch_synced_at` only on confirmed success — so a ClickHouse outage causes retry, never drift.

**Dependencies:** T0.2 (`ch_synced_at` outbox column), T0.3 (`ads_findings_history` `ReplacingMergeTree`), T3.3 (writes the PG rows + arms the outbox). Blocks accurate T3.4 reads (it populates the read model).

**Input Example:**
A batch of un-synced Postgres rows the worker claims:
```elixir
[%{finding_id: "fnd_2026-06-21_4242_0007", site_id: 4242, kind: "point", metric: "visitors",
   severity: "high", observed: 920.0, expected: 138.0, joint_z: 7.9, status: "open",
   detection_signature: "v:visitors|k:point|sev:high",
   explanation: "Visitors were 6.7x the expected 138 ...", ch_synced_at: nil, updated_at: ~N[2026-06-21 13:00:05]}]
```

**Output Example:**
One idempotent `ReplacingMergeTree` row inserted into ClickHouse, then the PG marker stamped:
```text
INSERT INTO ads_findings_history (site_id, finding_id, detected_at, kind, metric, severity,
  observed, expected, joint_z, detection_signature, explanation_headline, status, feedback,
  shadow, version)
VALUES (4242, 'fnd_2026-06-21_4242_0007', '2026-06-21 13:00:00', 'point', 'visitors', 'high',
  920, 138, 7.9, 'v:visitors|k:point|sev:high', 'Unusual traffic — 6.7x expected', 'open', '',
  0, /* version = */ 1718974805);
-- then in Postgres: UPDATE ads_findings SET ch_synced_at = now() WHERE finding_id = '...'
```

**Acceptance Criteria:**
- Runs on its own Oban queue/cron (e.g. every 1–2 min); claims a bounded batch of `ch_synced_at IS NULL` rows via the partial-scan index; inserts to CH; stamps `ch_synced_at` **only after** the CH insert is acknowledged.
- **Idempotent / at-least-once:** re-running on the same row (after a crash between CH insert and PG stamp) produces no duplicate logical finding — `ReplacingMergeTree(version)` collapses by `finding_id`; `version` is monotic (e.g. `updated_at` epoch) so the latest status wins.
- **No drift under failure:** a simulated ClickHouse outage leaves rows un-synced and the next run reconciles them; chaos test asserts eventual convergence with CH returning errors for N minutes.
- Status changes (resolve/feedback from T3.3) reset `ch_synced_at = NULL` and re-project a higher-`version` row, so the read model (T3.4) reflects current status within one sync interval.
- Lag is observable (telemetry: oldest un-synced age, batch size, failures).

**Key Code Snippet:**
```elixir
# lib/workers/anomaly_findings_sync.ex
defmodule Plausible.Workers.AnomalyFindingsSync do
  use Oban.Worker, queue: :anomaly_findings_sync, max_attempts: 10
  import Ecto.Query
  alias Plausible.{Repo, AnomalyDetection}
  @impl true
  def perform(_job) do
    rows = Repo.all(from f in "ads_findings", where: is_nil(f.ch_synced_at), limit: 1000)
    case AnomalyDetection.insert_history(rows) do        # idempotent CH insert (ReplacingMergeTree)
      :ok ->
        ids = Enum.map(rows, & &1.finding_id)
        Repo.update_all(from(f in "ads_findings", where: f.finding_id in ^ids),
          set: [ch_synced_at: NaiveDateTime.utc_now()])  # stamp ONLY after CH ack
        :ok
      {:error, reason} -> {:error, reason}               # Oban retries; rows stay un-synced (no drift)
    end
  end
end
```

**Required Skills:** Elixir; Oban (retries/backoff/uniqueness); transactional-outbox pattern; ClickHouse `ReplacingMergeTree` semantics; idempotent batch upserts.

**Required Knowledge:** The dual-write anti-pattern and outbox/CDC alternatives (ADR-10); Plausible's existing async ClickHouse write path (`lib/plausible/ingestion/write_buffer.ex`); `IngestRepo` insert; `ReplacingMergeTree` collapse + `FINAL`/`argMax` read semantics (T0.3).

**Self-Learning References:** Transactional outbox pattern (`https://microservices.io/patterns/data/transactional-outbox.html`); Oban reliability (`https://hexdocs.pm/oban/Oban.html`); ClickHouse ReplacingMergeTree (`https://clickhouse.com/docs/en/engines/table-engines/mergetree-family/replacingmergetree`); idempotent consumers (`https://microservices.io/patterns/communication-style/idempotent-consumer.html`).

**Estimated Effort:** 5 days.

**Risks:**
- **Sync lag visible in the dashboard (medium):** the read model trails the source of truth by up to one sync interval. Mitigation: short interval (1–2 min) + show "just now" findings optimistically from the T3.3 response; lag telemetry + alert.
- **Duplicate/again-delivery (low):** handled by `ReplacingMergeTree(version)` + monotonic version; a test inserts the same finding twice and asserts a single logical row after `FINAL`.
- **Backlog after a long CH outage (low):** bounded batch + parallel queue drains the backlog; the partial index keeps the claim query cheap.

---

### Task T4.1 — Contract & integration tests against the mock (CI)

**Objective:** Build the automated contract test suite asserting every `/v1` endpoint matches the OpenAPI schema, plus an Elixir↔ADS integration test against the mock, wired into CI.

**Dependencies:** T0.1 (contract + mock). Runs continuously thereafter.

**Input Example:** the Part-6.1 `/v1/detect` request fixture (running example).

**Output Example:**
Test assertion:
```python
def test_detect_contract(client, schema):
    r = client.post("/v1/detect", json=DETECT_REQ_4242)
    assert r.status_code == 200
    jsonschema.validate(r.json(), schema["DetectResponse"])     # must conform
    assert r.json()["anomalies"][0]["finding_id"] == "fnd_2026-06-21_4242_0007"
```

**Acceptance Criteria:**
- Every endpoint has a schema-conformance test (request and response).
- An Elixir test exercises `AnomalyDetection.Client` against the mock (ExUnit + Bypass).
- CI fails on any contract drift; runs on every PR in both repos.
- Parity test: MV aggregation for `site_id=4242` equals the Stats query engine result (guards T0.3).

**Key Code Snippet:**
```elixir
# Elixir side, using Bypass to fake ADS
test "client maps detect response" do
  Bypass.expect_once(bypass, "POST", "/v1/detect", fn conn ->
    Plug.Conn.resp(conn, 200, Jason.encode!(%{"anomalies" => [%{"finding_id" => "fnd_2026-06-21_4242_0007"}]}))
  end)
  assert {:ok, %{"anomalies" => [%{"finding_id" => "fnd_2026-06-21_4242_0007"}]}} =
           Plausible.AnomalyDetection.Client.detect(payload_4242())
end
```

**Required Skills:** pytest + jsonschema; ExUnit + Bypass; CI (GitHub Actions).

**Required Knowledge:** The OpenAPI contract (T0.1); Plausible's ExUnit test conventions; ClickHouse test fixtures (`fixture/`, `Plausible.TestUtils`).

**Self-Learning References:** jsonschema (`https://python-jsonschema.readthedocs.io/`); Bypass (`https://github.com/PSPDFKit-labs/bypass`); ExUnit (`https://hexdocs.pm/ex_unit/ExUnit.html`); schemathesis (`https://schemathesis.readthedocs.io/`).

**Estimated Effort:** 4 days.

**Risks:** Mock drifting from real ADS → mitigate by generating the mock *from* the OpenAPI spec and running the same contract suite against real ADS in Phase 2.

---

### Task T4.2 — Detection-quality harness (precision/recall, synthetic injection, backtest)

**Objective:** Build the offline evaluation harness that injects known anomalies into real historical series, measures precision/recall/lead-time per detector and fused, and gates model promotion (§5.6).

**Dependencies:** T1.* and T1.3 (detectors to score); T0.3 (historical features).

**Input Example:**
A labelled backtest case:
```python
{"site_id": 4242, "metric": "visitors",
 "series": series_4242_28d,
 "injected": [{"t": "2026-06-21T12:00:00Z", "type": "spike", "magnitude": 6.7}],
 "expected_label": "anomaly"}
```

**Output Example:**
Scorecard:
```json
{"detector": "fused", "precision": 0.83, "recall": 0.76, "f1": 0.79,
 "median_lead_time_min": 8, "false_positive_rate_per_week": 0.9,
 "by_kind": {"point": {"recall": 0.88}, "composition": {"recall": 0.62}}}
```

**Acceptance Criteria:**
- Generates synthetic spikes/drops/mix-shifts with controlled magnitude; computes precision/recall vs labels.
- Produces a per-version scorecard stored alongside `ads_model_registry.metrics`.
- Promotion gate: candidate must be non-inferior on recall AND lower FPR, with significance (bootstrap CI) — automated pass/fail.

**Key Code Snippet:**
```python
# quality/backtest.py
def evaluate(detector, cases):
    tp = fp = fn = 0
    for c in cases:
        flagged = detector(c["series"])
        hit = any(near(f["t"], inj["t"]) for f in flagged for inj in c["injected"])
        if c["expected_label"] == "anomaly":
            tp += int(hit); fn += int(not hit)
        else:
            fp += int(bool(flagged))
    prec = tp / (tp + fp or 1); rec = tp / (tp + fn or 1)
    return {"precision": prec, "recall": rec, "f1": 2*prec*rec/((prec+rec) or 1)}
```

**Required Skills:** Python; evaluation methodology; synthetic data generation; statistical significance.

**Required Knowledge:** Precision/recall/F1 & lead-time; the detector APIs (T1.*); anomaly-injection techniques.

**Self-Learning References:** Numenta NAB scoring (`https://github.com/numenta/NAB`); precision/recall (`https://scikit-learn.org/stable/modules/model_evaluation.html`); bootstrap CIs (`https://en.wikipedia.org/wiki/Bootstrapping_(statistics)`).

**Estimated Effort:** 7 days.

**Risks:** Synthetic anomalies not representative of real ones → mitigate by also curating a small hand-labelled set from real Plausible incidents (with privacy review).

---

### Task T4.3 — Privacy red-team, load/performance, and end-to-end tests

**Objective:** Verify the privacy invariants (no PII stored/logged/returned, k-anon enforced, no cross-day tracking), load-test ADS at target QPS, and run full E2E from ingestion → alert → feedback → suppression.

**Dependencies:** T2.2, T2.3 (privacy paths); T2.1 (service); T3.* (Elixir path).

**Input Example:**
Privacy red-team case:
```python
{"site_id": 4242, "samples": [
  {"event": "Signup", "key": "email", "value": "jane.doe@gmail.com"},
  {"event": "Signup", "key": "card_number", "value": "4111111111111111"}]}
# assert: raw values absent from response body, from app logs, from any DB row
```

**Output Example:**
E2E assertion outcome:
```text
[PASS] /v1/privacy_scan: raw email/PAN never present in response, logs, or ads_findings
[PASS] /v1/returning_visitors: cohort < 50 -> rates null, k_anonymity_ok=false
[PASS] load: 2,000 detect req/s sustained, p99 < 120 ms, 0 5xx
[PASS] E2E: spike for site 4242 -> finding -> email -> "Not an anomaly" -> next detect muted
```

**Acceptance Criteria:**
- Greps service logs and DB rows after privacy_scan; **fails** if any raw PII is found.
- k-anonymity floor enforced for returning-visitors (cohort < k → suppressed).
- Load test hits the §7 100K-stage target QPS with p99 latency SLO; reports throughput & errors.
- E2E test reproduces the full running-example journey through both repos.

**Key Code Snippet:**
```python
# tests/privacy/test_no_pii_leak.py
def test_no_raw_pii_anywhere(client, caplog, db):
    r = client.post("/v1/privacy_scan", json=PRIVACY_REQ_4242)
    body = r.text
    assert "jane.doe@gmail.com" not in body and "4111111111111111" not in body
    assert "jane.doe@gmail.com" not in caplog.text
    assert db.count_rows_containing("4111111111111111") == 0     # nothing persisted
```

**Required Skills:** pytest; load testing (k6/Locust); log/DB inspection; security testing mindset.

**Required Knowledge:** The privacy invariants (§4.4, §6.3, §6.4); the salt/anonymity model (`salts.ex`); E2E flow across Elixir + Python.

**Self-Learning References:** Locust (`https://locust.io/`); k6 (`https://k6.io/docs/`); OWASP ASVS (`https://owasp.org/www-project-application-security-verification-standard/`); pytest caplog (`https://docs.pytest.org/en/stable/how-to/logging.html`).

**Estimated Effort:** 7 days.

**Risks:** A privacy regression slipping through → mitigate by making the no-PII grep test a **blocking** CI gate on the Python repo and re-running it against real ADS in Phase 2.

---

## 9.1 Effort roll-up & dependency summary

| Owner | Tasks | Days |
|-------|-------|------|
| SSE-A | T1.1, T1.2, T1.3 | 14 |
| SSE-B | T0.1, T2.1, T2.2, T2.3, T2.4, T2.5 | 30 |
| ECE | T0.2, T0.3, T3.1, T3.2, T3.3, T3.4, T3.6 | 32.5 |
| FE (frontend-capable engineer; see T3.5 skill-gap) | T3.5 | 6 (+3–4 ramp if absorbed by a backend SSE) |
| QE | T4.1, T4.2, T4.3 | 18 |

**Resourcing note:** the staffed team (2 Python SSE, 1 Elixir/ClickHouse, 1 QE) has **no dedicated frontend engineer**, yet the in-dashboard insights panel (T3.5) is React/TypeScript. Options: borrow a frontend engineer; have a frontend-capable SSE absorb it with ramp time; or ship v1 without it (the email path T3.3 + HEEx settings T3.4 make the feature fully usable). T3.5 is deliberately kept **off the critical path** so this gap cannot block GA.

**Hard dependencies (must finish before dependent starts):** T0.1 → all; T0.2 → T2.4/T3.2/T3.3; T0.3 → T2.4/T3.2/T3.6; T1.1+T1.2 → T1.3; T1.* → T2.4/T4.2; T3.1 → T3.2; T3.3 → T3.6 (outbox feeds sync) → T3.4 (sync populates the read model the API serves); **T3.4 → T3.5** (SPA consumes the internal API). **Phase 0 (T0.1–T0.3) is the critical path; everything else parallelizes against the frozen contract. T3.5 is a fast-follow, not a GA blocker.**

---

# Part 10 — Compliance Matrix

## 10.1 Task-card section compliance

Required sections (in order): **Objective · Dependencies · Input Example · Output Example · Acceptance Criteria · Key Code Snippet · Required Skills · Required Knowledge · Self-Learning References · Estimated Effort · Risks**.

| Task | Obj | Deps | Input Ex | Output Ex | Accept. | Code | Skills | Knowledge | Refs | Effort | Risks | Complete |
|------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| T0.1 Contract+mock | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T0.2 Postgres schemas | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T0.3 ClickHouse MV | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T1.1 Point detector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T1.2 Composition detector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T1.3 Scoring+calibration | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T2.1 Service skeleton | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T2.2 Privacy detector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T2.3 Returning visitors | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T2.4 Train+registry | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T2.5 Feedback+explain | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.1 ADS client | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.2 AnomalyScan worker | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.3 Notify+feedback | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.4 Flag + HEEx settings + JSON API | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.5 React dashboard insights panel | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T3.6 Findings outbox + CH sync worker | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T4.1 Contract tests | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T4.2 Quality harness | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |
| T4.3 Privacy/load/E2E | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✅ |

**All 20 task cards contain all 11 required sections, in order, none blank.**

## 10.2 Brief-level requirement compliance

| Brief requirement | Where satisfied |
|-------------------|-----------------|
| Self-training | §1.3, §4.6, §5.1–5.3 |
| Detect+explain traffic/behaviour anomalies | §4.2, §4.3, §6.1 (explanation field) |
| Privacy data-leak risk | §4.4, §6.3, T2.2 |
| Returning users via multi-day sessions | §1.4, §4 (domain 4), §6.4, T2.3 |
| Python, integrated via API | Part 2, Part 6, `services/anomaly_detection/` |
| Hand-off to 2 SSE + 1 ECE + 1 QE | Part 9 (roles on every card) |
| Implementation plan w/ cross-engineer dependencies incl. Elixir/ClickHouse client-side | §9 graph, §9.1, T3.1–T3.4 (Elixir), T0.3 (ClickHouse) |
| Client-side UI matches real Plausible frontend (React/TS SPA dashboard + HEEx settings) | §2.2.1 (UI reuse rows), T3.4 (HEEx settings + JSON API), T3.5 (React `assets/js/dashboard/anomalies/`), Part 8 (file inventory); skill-gap resourcing flagged in §9.1 + T3.5 |
| Task cards w/ I/O examples, code, skills, knowledge, refs | Part 9 (all cards) |
| Compliance matrix | Part 10 |
| Every recommendation tied to existing component | §2.2.1 |
| New components justified + path reviewed | §2.2.2 |
| Storage/CPU/CH-query/network/inference estimates | §7.2 (tiered / power-law, not flat averages) |
| Power-growth 1K/100K/1M | §7.3 (burst-flattening, tail isolation, findings hot/cold split) |
| One running example throughout | header table + used in every payload |
| Option A/B/C per major decision | Part 3 (ADR-1…11) |
| API threats (authn/authz/privacy/abuse/mitigation) + req/resp | Part 6 (all endpoints) |
| Exact files/modules/packages/migrations | Part 8 |
| Cold-start/drift/retraining/feature-store/registry/A-B/rollback | §5.1–5.7 |
| Business-first then technical | Part 1 then Parts 2+ |
| Single markdown document | this file |

---

## Appendix A — Glossary (for non-technical readers)

- **Baseline:** the "normal" pattern ADS learns per site/metric/time-of-week.
- **Composition / mix-shift:** a change in the *make-up* of traffic (e.g. which browsers) even if the total is steady.
- **Salt / SipHash:** the cookieless mechanism Plausible uses to count unique visitors without identifying them; the secret is destroyed after 48h.
- **k-anonymity:** a privacy safeguard that suppresses any statistic computed on fewer than *k* people.
- **Stouffer's Z:** a principled way to combine multiple statistical signals into one score.
- **Shadow / canary / A-B:** progressively riskier ways to test a new model in production before fully trusting it.
- **MV (materialized view):** a ClickHouse table that pre-computes summaries as data arrives, so ADS reads cheap rollups instead of raw events.

*End of document.*
