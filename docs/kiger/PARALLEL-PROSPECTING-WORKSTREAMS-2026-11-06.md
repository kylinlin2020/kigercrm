# Parallel prospecting migration workstreams

Date: 2026-11-06
Branch: `kiger-dev`
PR: #1 (Draft; do not merge)
Delivery rule: implement the five workstreams independently behind the existing tenant, permission, evidence, and human-approval boundaries. This document is a coordination baseline, not a claim that the features are implemented.

## Shared invariants

- Reuse the existing CRM Campaign, Strategy, Run, Candidate, Evidence, Score, Lead and Customer records. Do not introduce a second CRM master-data model.
- Every new row and query is tenant-scoped. Owner/data-scope authorization is checked in application services.
- Provider output is untrusted evidence. AI-generated summaries or discovered fields never become verified facts without authoritative evidence.
- Candidate creation does not create a Lead. Existing approval and conversion workflow remains the only path into CRM.
- No automatic outbound email, WhatsApp, or website publishing. Any external action requires explicit human approval.
- Every asynchronous operation must be durable, idempotent, restart-safe, bounded, and observable. No process-local-only queues or mission state.
- Paid APIs remain opt-in, require tenant-provided credentials, and must enforce per-tenant quotas and cost ceilings.
- Keep `main` unchanged. Work only on `kiger-dev`; keep PR #1 Draft.

## Workstream A — Durable asynchronous provider execution (P0)

### Current verified boundary
The branch contains the async provider capability contract, extraction-job entity and PostgreSQL repository, submission-intent/idempotency primitives, status mapping/state machine, expiry maintenance worker, page validator, and atomic page/candidate/evidence/score persistence. The synchronous Run worker explicitly rejects async-only providers before invoking any provider, so there is no false-success path.

### Remaining implementation
1. Add an immutable, tenant-scoped extraction request snapshot. It must contain the canonical provider request, Run/plan identity, excluded-domain snapshot, and request fingerprint; do not reconstruct it later from mutable Campaign/Strategy rows.
2. Implement a dedicated extraction consumer with tenant scope established before resolving repositories or providers.
3. Implement Submitting -> Pending acknowledgement with the persisted idempotency key. If provider acceptance is ambiguous and the provider cannot authoritatively recover by that key, transition to NeedsReview; never blindly resubmit.
4. Poll only due jobs. Renew the lease around provider I/O; persist every transition with token + fencing version. Use provider Retry-After when valid, otherwise bounded exponential backoff.
5. Fetch bounded pages; normalize and deduplicate identities across pages; commit records/evidence and cursor advancement atomically. Coverage advances only after a complete successful extraction.
6. Link child extraction terminal outcomes back to the parent Run and prevent Run success while any child is unresolved, partial, failed, or NeedsReview.
7. Implement an authenticated, tenant-scoped, audited operator reconciliation endpoint for NeedsReview. Require authoritative provider identity evidence and immutable audit history.

### Acceptance gates
- Duplicate submission, ambiguous timeout, process restart, stale lease takeover, unknown provider state, cursor replay, duplicate records across pages, partial page failure, tenant isolation, and parent Run continuation tests.
- PostgreSQL tests use two independent physical connections. SQLite/source-contract tests are not substitutes.
- Until the full lifecycle passes, async-only providers remain unavailable to normal Run execution.

## Workstream B — Super Search (P0/P1)

### Scope
Port the source project's multi-round Super Search mission semantics into KIGER's existing Campaign/Strategy/Run model. Do not copy the source project's parallel mission-owned CRM store.

### Implementation slices
1. Add a preview/planning service that validates products, markets, customer types, provider capabilities, depth, target count, time budget, and optional cost ceiling before any Run is created.
2. Generate deterministic round plans with distinct query themes (baseline, local-language, procurement, project/engineering, trade channel, OEM/integration, vendor registration, directories/associations, aftermarket, contract awards, expansion signals, certification, regional long-tail, application specialists, replacement demand, coverage recovery).
3. Persist mission/round/event state in tenant-scoped tables, with idempotent round creation and optimistic concurrency (ETag/revision).
4. Execute rounds through the existing Run/Provider execution pipeline. Each round has explicit result, time, and cost budgets; stop on target, deadline, cost ceiling, cancellation, or convergence.
5. Provide mission detail, round history, progress and event APIs/UI. Counts must be computed from persisted Candidate identities, not estimated from raw provider results.
6. Keep AI query enhancement optional and bounded. Store original and enhanced plan fingerprints and never let AI expand provider permissions or cost limits.

### Acceptance gates
- Preview rejects unavailable/disabled provider capabilities and invalid cost/currency combinations.
- Round plans are deterministic, bounded, non-duplicative, and preserve exclusions.
- Optimistic concurrency rejects stale updates; tenant/owner access is enforced.
- Cancellation and restart resume from persisted round state; no duplicate Run/round execution.
- Budget ceilings cannot be exceeded by retries or provider fan-out.

## Workstream C — Deep Mining (P1)

### Scope
Deep Mining expands already reviewed/eligible root Candidates into a bounded evidence graph of related companies, buyers, suppliers, distributors, contractors, projects and contact channels. It is not an unrestricted recursive crawl.

### Implementation slices
1. Add tenant-scoped mining state, nodes, edges, tasks and evidence links; deterministic IDs make task creation idempotent.
2. Root nodes must reference existing Candidates. Child nodes are created only through the existing candidate normalization and deduplication pipeline.
3. Implement bounded depth profiles (balanced/deep/extreme) with explicit root, query, website-probe, provider, time and cost limits.
4. Plan each child query around the root company and a typed relationship hypothesis. Preserve country/language scope and exclusion rules.
5. Website probes use only the shared safe HTTP boundary and public URLs; no login, paywall, CAPTCHA or access-control bypass.
6. Relationship type is a hypothesis until corroborated. Persist source evidence and field authority; conflicts lower confidence and require review.
7. Stop on duplicate saturation, no new evidence, budget/deadline, cancellation, or maximum depth. Resume from durable task state.

### Acceptance gates
- Graph cycles, duplicate candidate identities, cross-tenant references, untrusted URLs, unsupported relationship inference, depth overflow and budget exhaustion are tested.
- Deep Mining cannot silently upgrade verification status or directly convert/create Leads.
- All root and child evidence remains traceable to canonical source URLs and retrieval timestamps.

## Workstream D — Website Discovery (P1)

### Scope
Discover and validate a company's public official website using authoritative registry data first, then constrained search evidence. A domain is not official merely because its name resembles the company.

### Implementation slices
1. Add a website discovery service contract returning candidate URLs, source, match rationale, confidence, and field-level evidence.
2. Prioritize official registry website fields and known company identifiers. Search-provider results are discovery evidence only.
3. Normalize IDN/hostnames, reject credentials and non-HTTP(S) schemes, and validate every network target through the shared DNS/IP-pinned transport boundary.
4. Apply bounded redirects only if each hop is revalidated; otherwise keep redirects disabled. Cap response bytes, time, concurrency and per-company probes.
5. Detect parked domains, marketplaces, social profiles, directory pages, unrelated redirects and conflicting legal names. Preserve rejected candidates with safe reason codes.
6. Persist discovered domains as unverified evidence. Require independent corroboration before changing a canonical official website.
7. Expose review detail in the Candidate workspace; no automatic website publishing or outreach.

### Acceptance gates
- SSRF/private/link-local/loopback/DNS rebinding, redirect-to-private-host, IDN, malformed URL, oversized response, timeout and domain-conflict tests.
- A positive match requires reproducible evidence and cannot rely on AI-only classification.
- Tenant data and provider credentials never appear in logs or response URLs.

## Workstream E — Scheduling and Coverage Memory (P1)

### Scope
Scheduling creates future Runs using an approved Strategy; Coverage Memory records what scope/source/time windows were actually searched and which canonical identities were observed. These are separate concerns and must not infer successful coverage from a queued or partial Run.

### Implementation slices
1. Add tenant-scoped Schedule definitions with timezone, recurrence, next-run timestamp, enabled state, owner, campaign/strategy version, and concurrency policy.
2. Create due Runs using a durable idempotency key derived from Schedule + scheduled occurrence. A missed tick must not create duplicate Runs.
3. Add pause/resume, next-run preview, bounded catch-up policy and per-tenant active schedule limits.
4. Add Coverage Memory keyed by tenant, provider, canonical scope fingerprint, and normalized external identity. Store first/last seen, last complete scan, source record identity and evidence links.
5. Coverage updates occur only in the same transaction that commits a fully complete provider result. Failed, truncated, cancelled, partial or NeedsReview work cannot advance the complete-scan watermark.
6. Use overlap windows for incremental scans; keep the coverage fingerprint independent of the moving time lower-bound, while retaining the exact scan window in each attempt.
7. Use memory to suppress redundant provider work and identify stale observations, not to delete Candidates or hide new evidence.

### Acceptance gates
- DST/timezone transitions, missed ticks, duplicate scheduler delivery, schedule edits during queued execution, cancellation, pause/resume and tenant isolation.
- Coverage watermark cannot advance on partial/failed results and is transactionally consistent with persisted evidence.
- Repeated scans link to canonical Candidate identity without overwriting review or conversion state.

## Parallel integration order

| Track | First deliverable | Depends on | Integration checkpoint |
|---|---|---|---|
| A Async execution | Immutable request snapshot + Submit/Poll consumer | Extraction persistence contracts | Before any async provider is enabled |
| B Super Search | Preview + deterministic round planner | Existing Strategy/Run APIs; A for async providers | First multi-round Run end-to-end |
| C Deep Mining | Durable graph/task model + bounded planner | Candidate dedupe/evidence; D for probes | One root Candidate with reviewed child evidence |
| D Website Discovery | Safe discovery contract + verifier | Shared DNS/IP-pinned transport | One official-domain decision with evidence |
| E Schedule/Coverage | Durable Schedule + complete-scan ledger | Run idempotency; A for async sources | Restart-safe scheduled Run with coverage commit |

## Delivery status

At the time this plan is added:
- A has substantial persistence and safety foundations but does not yet have a complete Submit/Poll/Fetch/Project consumer or operator reconciliation API.
- B, C, D and E require target-side implementation; source-project functionality is reference behavior, not proof of target completion.
- No .NET build/test or PostgreSQL dual-connection runtime validation is claimed by this plan.
- Windows-native acceptance remains a final gate; do not mark the migration complete until it passes.


## Implementation increments after baseline

### Super Search planner foundation
- Added `CrmProspectingSuperSearchPlanner` as a pure deterministic planning component.
- Supports Balanced/Deep/Extreme profiles capped at 4/8/16 rounds, 16 query themes, bounded provider fan-out, normalized scope, and stable SHA-256 scope/round/cell fingerprints.
- Carries excluded terms and domains into each persisted-ready round plan. It does not call providers or create Runs.
- Added unit-test source covering depth caps, determinism, unique round/cell fingerprints, exclusion propagation, normalization, invalid scope/fan-out and fingerprint changes.
- Commits: `23eddd535b80524716f53df3dbb7c6d404a24ea7`, `f85a811568094aa5e8065ec7381d1289fabc75e3`.

### Deep Mining policy foundation
- Added `CrmProspectingDeepMiningPolicy` with bounded depth/root/query/website-probe budgets matching the three depth profiles.
- Added deterministic mission/root/candidate task IDs and a relationship-query builder that preserves product/company/market scope and explicit exclusions.
- Added stop decisions for cancellation, deadline, cost ceiling, duplicate saturation, root/depth/query/probe limits, and empty root sets.
- Added unit-test source for policy profiles, terminal stop reasons, resource limits, deterministic task identity, query scope and control-character rejection.
- Commits: `99045e641e006b7b38a41fe8f9df9fb85341b93f`, `e9b10b374e670ab21b76c276a93e99d42d0eccc7`, `cc62fbf9dfada8a8633e63afa71f7378afaff5be`.

**Validation boundary:** these are domain foundations and test source, not completed end-to-end Super Search or Deep Mining workflows. They are not yet exposed through application APIs/UI, persisted as missions/graphs, or connected to the Run/website-probe pipeline. No .NET build/test has been executed in this environment.


### Website Discovery candidate-policy foundation
- Added `CrmProspectingWebsiteDiscoveryPolicy` to validate HTTPS URL shape, default port, userinfo/query/fragment rejection, hostname syntax, IP-literal/local/internal hosts, and non-corporate social/directory platforms.
- Candidate match classes distinguish registry name matches, search title/host matches, map matches and manual candidates. Every accepted candidate remains review-only; the policy never sets an official website.
- Added tests for positive-but-unverified candidates, registry/manual review boundaries, unsafe URL forms, localhost/private/IP literals (IPv4 and IPv6), social platforms, weak matches and control characters.
- Commits: `e61e345f197659671b4cab38d64638ced3c40496`, `a637fa198b5d3e9652b6a235fb4c7603d3b93b63`, `14a1bf34762fc758d3ca4fe9b64d2c59ed97a6a9`, `1b612f39cde13c8df76784e4d373525bab21833c`, `66751e95f4fd84fa0cf7075cbc5368d8ff484e0d`, `2b3460bbc8e309d1ddad1e0680fc25173d917d57`.

**Validation boundary:** this is URL/candidate intake policy only. It does not fetch pages, resolve DNS, validate redirects, persist website evidence, or expose an application API/UI. Callers must use the existing DNS/IP-pinned HTTP transport before network access. No .NET build/test has been executed.


### Coverage Memory policy foundation
- Added `CrmProspectingCoverageMemoryPolicy` with deterministic stable scope fingerprints that normalize and sort search scope fields while keeping moving scan windows separate.
- Added provider + normalized external-record observation keys that remain stable across retrieval attempts.
- Added a fail-closed complete-watermark predicate: only Completed, fully paged, zero-failure, non-truncated, non-cancelled, non-review work may advance coverage.
- Added tests for order/whitespace stability, scope changes, retry-stable observation identity and every incomplete/failed watermark condition.
- Commits: `62481631d25d21cab9c3ec457ef537a5e3a6418e`, `87fb1d1c56acb751b79e8cb85a15d91ea41d4360`.

**Validation boundary:** this is identity/policy code only. It is not backed by a coverage table/repository, is not wired into provider request finalization, and cannot advance a watermark. Schedule entity, recurrence worker, durable idempotency and API/UI are still outstanding. No .NET build/test has been executed.


### Website Discovery transport regression coverage
- Added focused tests for the shared DNS/IP-pinned HTTP transport's public-address filter, including private, loopback, link-local, carrier-grade NAT, documentation, benchmarking, multicast, broadcast, IPv4-mapped IPv6, IPv6 ULA/link-local/documentation, and representative public IPv4/IPv6 addresses.
- Removed a duplicate IPv4 special-use range predicate from the transport filter.
- This hardens the shared network boundary used by future website probes; it does not yet implement page fetching, bounded response streaming, redirect-hop revalidation, or evidence persistence.
- Commits: `24f2b957c84f817b41b7842561a1bdb70a683e92`, `f09cf8f337f859a77e1c49642d1c90477e742884`, `f43964523fd671ed6e07896167e20f6e1693ad2e`.

**Validation boundary:** tests are committed but not executed here because the environment has no .NET SDK. Windows-native build and runtime verification remain required.


### IPv6 special-use prefix correction
- Corrected the public-address filter's RFC 9637 documentation allocation check for `3fff::/20`. The previous prefix helper call supplied too few prefix bytes for a /20 match and therefore did not reject that range.
- Added a regression vector for `3fff::1`. Added an upper-bound rejection vector (`3fff:0fff::1`) and a just-outside acceptance vector (`3fff:1000::1`) to verify the /20 boundary.
- Commits: `cead645a8600f73a0fc843e52daaab3a81d4ff52`, `c7c7573721949dcf3065206f6f8d5e787a9502c7`.
- Tests remain unexecuted in this environment; Windows/.NET validation is still required.
