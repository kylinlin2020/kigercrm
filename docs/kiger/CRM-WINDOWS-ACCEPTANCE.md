# KIGER CRM Windows Acceptance Runbook

> Status: procedure only. This document does not claim that any command, migration, or runtime scenario has passed. Record the actual command output and environment details during the acceptance run.

## 1. Scope and release boundary

- Repository: `kylinlin2020/kigercrm`
- Acceptance branch: `kiger-dev`
- Pull request: #1, targeting `main`
- Keep the pull request in Draft until every required gate below has evidence.
- Do not merge or modify `main` as part of this runbook.
- The CRM is integrated into KIGER. A separate Go CRM REST connector is out of scope.

The acceptance covers the CRM customer lifecycle and the quote → sales order → shipment fulfillment path, plus tenant isolation, permissions, field security, frontend routing, and upgrade safety.

## 2. Windows prerequisites

Run from PowerShell on a clean Windows 11 workstation (or a documented clean Windows build agent).

Verify and record:

```powershell
git --version
dotnet --info
node --version
corepack --version
```

The frontend declares Node.js >= 24 and pnpm >= 11, with pnpm pinned by `packageManager` in `frontend/package.json`. Use Corepack to activate the repository-pinned package manager; do not globally replace the pinned version.

PostgreSQL must be a supported project version and reachable using a dedicated test database. Record the server version, database name (not credentials), and whether tenant databases are shared-schema or database-per-tenant.

## 3. Pull the exact candidate commit

```powershell
git clone https://github.com/kylinlin2020/kigercrm.git
Set-Location kigercrm
git fetch origin
git checkout kiger-dev
git pull --ff-only origin kiger-dev
git rev-parse HEAD
git status --short
```

Record the resulting SHA. It must match the PR head being accepted. The working tree must be clean before validation.

## 4. Backend restore, build, and tests

From the repository root:

```powershell
dotnet restore backend/XiHan.BasicApp.slnx
if ($LASTEXITCODE -ne 0) { throw "Backend restore failed" }

dotnet build backend/XiHan.BasicApp.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Backend build failed" }

dotnet test --solution backend/XiHan.BasicApp.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Backend tests failed" }
```

Save complete console output and test result files. Do not convert skipped, undiscovered, or unexecuted tests into a pass.

### Required CRM test evidence

- Lead normalized identity-key persistence and duplicate-candidate query behavior.
- Primary-contact uniqueness for one tenant/customer, multiple non-primary contacts, and independent tenant/customer combinations.
- Lead conversion transaction rollback and concurrent conversion: exactly one winner, no orphan customer/contact from the losing transaction.
- Quote amount rounding, revision immutability, status transition guards, and concurrent revision uniqueness.
- Accepted quote conversion: one order per quote, copied line snapshots, quote/order atomicity, and rollback on a forced failure.
- Order draft edits: totals recalculated from persisted active lines; source-quote lines remain immutable; historical shipment references are retained.
- Shipment reservation, cancellation release, dispatch consumption, delivery timestamp, and status roll-up.
- Concurrent shipment creation/dispatch on the same order: no line can exceed ordered quantity; stale writes roll back the complete transaction.
- Permission, tenant, owner/assignee data scope, and field-level masking/query guards on reads and writes.

Source-wiring tests are useful regression guards but are not substitutes for repository-backed integration tests. SQLite repository tests for sales-order/shipment creation fingerprints verify CodeFirst persistence and tenant-scoped lookup; they do not prove PostgreSQL migration behavior, transaction rollback, or concurrent first-request recovery.

## 5. PostgreSQL migration acceptance

1. Take and verify a restorable backup of the test database before starting.
2. Confirm the database's recorded migration version and whether the CRM tables exist.
3. Run the application using the normal upgrade mechanism against the test database.
4. Inspect the migration ledger and confirm each applicable migration from 5.9.2 through 5.9.10 is recorded as successful.
5. Inspect the resulting columns, constraints, and indexes, especially:
   - Lead normalized email, phone-digit, and website-host keys and tenant-prefixed lookup indexes.
   - Contact primary-customer slot and uniqueness behavior.
   - Quote version, source-quote, and source-quote-line uniqueness.
   - Order fulfillment revision and reserved/shipped quantity columns and check constraint.
   - Permanent tenant-scoped quote/version, sales-order-number, and shipment-number uniqueness.\n   - Nullable `creation_request_fingerprint` columns on sales orders (5.9.9) and shipments (5.9.8); existing rows must remain NULL.
6. Re-run startup after successful migration and verify it is safe/idempotent.
7. Test a disposable legacy database containing a deliberate preflight conflict. The migration must stop with an actionable error and must not leave a partially upgraded schema.
8. Verify both platform and isolated tenant database paths. Scripts touching platform-only tables must safely skip when those tables do not exist in a tenant database.

Never test destructive migration failure scenarios against production data.

## 6. Frontend install, validation, and build

```powershell
Set-Location frontend
corepack enable
corepack pnpm install --frozen-lockfile
if ($LASTEXITCODE -ne 0) { throw "Frontend install failed" }

corepack pnpm run check
if ($LASTEXITCODE -ne 0) { throw "Frontend checks failed" }

corepack pnpm run test
if ($LASTEXITCODE -ne 0) { throw "Frontend tests failed" }

corepack pnpm run build
if ($LASTEXITCODE -ne 0) { throw "Frontend production build failed" }
```

Record Node and pnpm versions, command output, test totals, and build artifact result.

## 7. Runtime smoke test

Run the WebHost with a dedicated test configuration and PostgreSQL database. Do not commit secrets or production connection strings.

Verify:

- Authentication, tenant selection, menu seeding, and route resolution.
- All nine CRM routes: customers, contacts, activities, leads, opportunities, tasks, quotes, sales orders, and shipments.
- Page visibility and create/update/delete/execute buttons match the caller's permissions.
- A user without record access cannot read or mutate another owner's records by guessing IDs or calling APIs directly.
- Tenant A cannot read, mutate, or infer Tenant B's CRM records.
- Field masking applies to both list and detail endpoints.
- The UI can create and inspect a lead, convert it to a customer/contact, record an activity, create an opportunity, create and revise a quote, accept and convert the quote, edit an eligible draft order, create multiple partial shipments, dispatch them, and mark them delivered.
- Refreshing each route preserves correct server-backed state; no success toast is shown for a failed API operation.
- Logs contain no unhandled exception, cross-tenant data leak, or failed migration.

Use non-production sample data and retain request/response correlation IDs with secrets and personal data redacted.

## 8. Business invariants to verify manually

| Scenario | Expected result |
| --- | --- |
| Two active primary contacts for one tenant/customer | Second promotion is rejected; no silent replacement |
| Convert a non-Accepted quote | Rejected; no order created |
| Convert the same accepted quote twice | At most one order exists |
| Revise a Draft, Accepted, or Converted quote | Rejected |
| Edit a quote-derived order line | Rejected where it would alter the accepted quote snapshot |
| Reserve more than remaining order quantity | Rejected |
| Cancel a Draft shipment | Its exact reservation is released |
| Cancel a Dispatched shipment | Rejected |
| Dispatch a shipment twice | Second dispatch is rejected |
| Deliver before dispatch | Rejected |
| Complete an order manually while quantities remain | Rejected; fulfillment states are shipment-derived |
| Retry a document-number allocation with the same key and payload | Same allocated number is returned |
| Reuse a numbering key with a different payload | Rejected |
| Lose the response after CRM document commit and retry | Current numbering key alone does not guarantee full document replay; verify the documented duplicate conflict behavior |

The current fingerprint mechanism covers sequential retries only when server-side numbering is used. It is not a general request-key ledger, does not cover manually numbered creates, and does not yet replay the winner for concurrent first submissions. PostgreSQL multi-connection integration tests and explicit concurrent-winner recovery remain release blockers.

## 9. Evidence and release decision

Attach or link:

- Candidate commit SHA and clean working-tree output.
- Backend restore/build/test logs and test result summary.
- Frontend install/check/test/build logs.
- PostgreSQL version, migration ledger, index/constraint inspection, and backup/restore evidence.
- Runtime smoke-test checklist with pass/fail and defect references.
- Concurrency-test results from multiple database connections.
- Known limitations and unresolved defects.

Release gates:

- [ ] Exact PR head pulled on Windows.
- [ ] Backend restore succeeds.
- [ ] Backend Release build succeeds.
- [ ] All backend tests execute and pass.
- [ ] Frontend frozen install, check, tests, and production build pass.
- [ ] PostgreSQL migrations succeed on a legacy test database and are safe on a fresh database.
- [ ] Tenant isolation, permission, owner scope, and field security pass.
- [ ] Quote/order/shipment transaction and concurrency tests pass.
- [ ] Nine CRM routes pass runtime smoke tests.
- [ ] No unresolved blocker or data-integrity defect remains.

Only after every gate has evidence should PR #1 be considered ready for review and merge. Until then, keep it Draft and leave `main` unchanged.
