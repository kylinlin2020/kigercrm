# Haituo CRM → KIGER CRM Function Parity Audit — 2026-11-06

## Scope and method

This is a source-tree and source-contract audit, not a runtime acceptance report.

- Source repository: `hongshuoguo2-cloud/haituo-crm`, inspected tree SHA `85a582ad4f113dd88ea306636661b8cb56fcd02d`.
- Target repository: `kylinlin2020/kigercrm`, inspected branch `kiger-dev`, HEAD `31e14df332978914bb01418377f4d8a544b147b1`.
- The source contains TypeScript backend modules, frontend pages, agent skills/knowledge, worker infrastructure and tests. The target is a .NET CRM module with a Vue frontend and an existing integrated CRM.
- A matching filename or entity is not treated as proof of behavioral parity. Each status below distinguishes implemented target foundations from missing end-to-end behavior.

## Executive result

**The migration is not yet proven function-complete.** The target contains a substantial CRM and prospecting implementation, including campaign/strategy/Run lifecycle, provider adapters, candidate/evidence/score persistence, review and lead conversion, request ledger, worker lease/fencing, and a growing async extraction foundation.

The most important remaining functional gaps found in this pass are:

1. Recurring prospecting schedules and their scheduler/approval lifecycle are not represented by an equivalent target schedule aggregate/worker.
2. Contact enrichment, staged identity bootstrap and the full verification report workflow are only partially represented by target identity/website foundations.
3. Outreach sequences, touchpoint/event tracking, reply classification and follow-up automation are not shown as migrated end-to-end. Platform email configuration/sending is infrastructure, not parity with CRM outreach workflows.
4. Market analysis runs, trade observations and market opportunity facts/list are not represented in the target CRM module found in this tree.
5. Customer intelligence suggestions and public-pool ownership lifecycle require explicit behavioral parity checks against the existing target CRM; do not create a second customer/lead/contact CRM to fill these gaps.
6. Async extraction has durable contracts/persistence/policies, but still lacks a registered async provider adapter and a dedicated Submit/Poll/Fetch consumer with parent-Run continuation.

## Function parity matrix

| Source capability | Target evidence found | Status | Required next work |
|---|---|---|---|
| CRM customers, contacts, opportunities, quotes, orders, shipments | Existing target CRM entities, application services, query services, repositories and frontend API modules | **Existing CRM — reuse** | Compare business rules and field/permission behavior only; do not migrate a duplicate CRM module |
| Prospect campaigns and strategies | `SysCrmProspectingCampaign`, `SysCrmProspectingStrategy`, app-service lifecycle, approval and activation | **Migrated foundation** | Verify UI coverage, concurrency and full acceptance |
| Prospect Runs and provider execution plan | `SysCrmProspectingRun`, `SysCrmProspectingRunProviderExecution`, immutable snapshot, queue worker and recovery | **Migrated foundation** | Complete provider failure/restart and multi-instance PostgreSQL acceptance |
| Provider discovery/dispatch | Multiple opt-in public-data/search provider adapters, provider registry and request ledger | **Migrated in part** | Compare source provider catalog and credentials/rate-limit semantics provider by provider |
| Candidate pipeline, evidence and scoring | Candidate/Evidence/Score entities, factories, scorecard, idempotent persistence and review APIs | **Migrated foundation** | Validate all source ranking/qualification rules and frontend parity |
| Candidate review and CRM lead conversion | Review/duplicate handling and approved-candidate conversion methods in target app service | **Migrated foundation** | Verify authorization, transaction rollback and duplicate-conversion integration |
| Super search planning | Deterministic target planner with depth/fan-out/exclusion controls | **Policy migrated; workflow partial** | Persist planner missions, expose APIs/UI and connect planner output to Run execution |
| Deep mining | Target bounded depth/root/query/probe policy | **Policy migrated; workflow partial** | Persist graph/tasks and connect website probes, evidence and Run lifecycle |
| Website discovery | Target URL policy, review-only classification and safe DNS-pinned transport foundation | **Policy/transport partial** | Implement bounded page fetch, redirect revalidation, evidence persistence and review UI |
| Legal identity resolution | Target `ICrmProspectingIdentityResolver` and GLEIF exact-name resolver | **Partial** | Port remaining authority profiles, staged bootstrap, evidence history and operator workflow |
| Contact enrichment | Source has extraction, evidence ranking and enrichment attempts | **Not demonstrated in target** | Port bounded contact extraction/enrichment, provenance, verification levels and retry workflow |
| Prospect verification | Source has field-authority rules and a structured verification report | **Not demonstrated in target** | Port field-level verification policy/report and tests; keep unverified data from becoming authoritative |
| Coverage memory | Target deterministic scope fingerprint and complete-scan watermark policy | **Policy migrated; persistence partial** | Add durable coverage rows, provider finalization and scheduling integration |
| Async extraction | Target contracts, job table/repository, leases/fencing, poll/page policies and atomic page projection | **Foundation only** | Add a real opt-in adapter, dedicated consumer, Run continuation, operator NeedsReview path and PostgreSQL E2E tests |
| Recurring prospect schedules | Source `prospect-schedules.ts` and `prospect-scheduler.ts` implement daily/weekly/monthly timezone-aware recurrence and recurring-cost approval | **Missing in target** | Add schedule aggregate/API/UI, timezone-safe due calculation, idempotent Run creation and scheduler worker |
| Outreach sequences | Source `outreach-sequences.ts` and tests | **Missing in target** | Port sequence definitions, enrollment, step scheduling, suppression and approval gates |
| Outreach touchpoints/replies/follow-up | Source `prospect-outreach.ts` records inbound/outbound touchpoints, engagement events, reply classifications and follow-up tasks | **Missing in target** | Add prospect-linked touchpoint/event model and follow-up workflow; require human approval for external sends |
| Email engagement tracking | Source `email-tracking.ts`, `email-touchpoints.ts`, delivery security and tracking provider | **Missing in target** | Port signed tracking events, message identity, event dedupe and privacy/security rules |
| Market analysis | Source `market-analysis-runs.ts` with provider execution and idempotent job lifecycle | **Missing in target** | Add separate market-analysis domain/application workflow or explicitly defer as a product-scope decision |
| Trade observations and market opportunities | Source `market-trade-observations.ts`, `market-opportunity-facts.ts`, opportunity list/facts modules | **Missing in target** | Port observation identity, normalized facts, evidence, snapshots and query APIs |
| Customer intelligence suggestions | Source `customer-intelligence.ts` generates evidence-backed field suggestions | **Not demonstrated in target** | Integrate suggestions into existing customer records with field-level review; never silently overwrite CRM values |
| Customer public pool / ownership lifecycle | Source `customer-public-pool.ts` has release/claim/ownership/version guards | **Existing CRM parity unverified** | Compare target ownership/assignment/claim rules; extend existing CRM only where a real behavior is absent |
| Shipment OCR | Source `shipment-ocr.ts` | **Not demonstrated in target** | Check whether target document/OCR services cover the same extraction and confirmation workflow |
| Agent skills and knowledge | Source `agent-skills/*` and `agent-knowledge/*` | **Not assessed as application parity** | Decide whether these are product requirements for KIGER CRM or source-runtime-specific assets |

## Important architecture decisions

- The target already owns CRM Customer/Contact/Lead/Opportunity/Quote/Sales Order/Shipment capabilities. Reuse these entities and their authorization/data-scope rules. Do not import a parallel customer database or the previously excluded standalone CRM item.
- Prospect candidates must remain separate from approved CRM Leads until the existing explicit review/conversion workflow authorizes conversion.
- Outbound email, WhatsApp and other external messages must remain behind human approval and auditable send authorization.
- A provider adapter being present does not mean its data source is configured or operational; opt-in configuration and provider-specific tests remain required.
- Async extraction must remain disabled for Run plans until the adapter, consumer, parent-Run continuation and end-to-end acceptance are complete.
- This audit does not authorize publishing to any website or sending external communications.

## Parallel work plan

1. **Schedule lane:** design schedule persistence and recurrence semantics without touching the Run worker until idempotent enqueue/recovery contracts are specified.
2. **Enrichment/verification lane:** map source evidence authority and contact attempt models to target Candidate/Evidence; avoid writing authoritative CRM fields automatically.
3. **Outreach lane:** design prospect touchpoint and sequence entities against the target's existing messaging infrastructure; enforce approval before sending.
4. **Market intelligence lane:** inventory source provider contracts and data ownership; propose a separate bounded domain rather than coupling market observations to customer records.
5. **Async extraction lane:** implement adapter/consumer only after parent-Run durable continuation is fully specified; maintain fail-closed behavior meanwhile.
6. **Parity validation lane:** compare existing customer pool, customer intelligence and shipment OCR behavior before deciding whether code migration is necessary.

## Validation limits

- This audit used repository tree and source-file inspection only.
- No .NET restore/build/test, frontend install/build/test, PostgreSQL migration/integration test or Windows real-machine acceptance is claimed.
- PR #1 remains Draft and GitHub reports `mergeable=false`; this audit does not attempt a merge or rebase.
