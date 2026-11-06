# CRM conversion concurrency acceptance (PostgreSQL)

This is a **manual database-semantics check**, not an application integration test. It exercises PostgreSQL's conditional `UPDATE`, row-lock wait, READ COMMITTED predicate recheck, and rollback behavior using the same status predicates as the CRM repositories. It does not prove that the application's SqlSugar repository or UnitOfWork is wired to the same connection; that remains an application-level test gate.

Run only against a disposable/test database after applying the CRM migrations. Never run against production data.

## Preconditions

1. Have two independent terminals with `psql` connected to the same test database.
2. Prepare one test Lead in tenant `T` with status New (0), and note its `basic_id`.
3. Prepare one test Prospecting Candidate in tenant `T` with status Approved (2), and note its `basic_id`.
4. Confirm the rows before each scenario:

```sql
SELECT tenant_id, basic_id, status, converted_customer_id, converted_contact_id
FROM crm_lead WHERE tenant_id = T AND basic_id = LEAD_ID;

SELECT tenant_id, basic_id, status, converted_lead_id
FROM crm_prospecting_candidate WHERE tenant_id = T AND basic_id = CANDIDATE_ID;
```

Replace `T`, `LEAD_ID`, and `CANDIDATE_ID` with numeric test values. The database stores unquoted entity table names in lowercase.

## Scenario A — Lead conversion winner / loser

**Terminal A**: claim the Lead, keep the transaction open, then simulate successful conversion and commit.

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;

UPDATE crm_lead
SET status = 5
WHERE tenant_id = T
  AND basic_id = LEAD_ID
  AND status IN (0, 1, 2);
-- Expect UPDATE 1.

-- Keep this transaction open while running Terminal B's UPDATE.
UPDATE crm_lead
SET status = 4
WHERE tenant_id = T
  AND basic_id = LEAD_ID
  AND status = 5;
-- Expect UPDATE 1.

COMMIT;
```

**Terminal B**: while Terminal A is paused after its first UPDATE, run:

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;

UPDATE crm_lead
SET status = 5
WHERE tenant_id = T
  AND basic_id = LEAD_ID
  AND status IN (0, 1, 2);
-- This statement should wait for Terminal A, then report UPDATE 0.

SELECT status, converted_customer_id, converted_contact_id
FROM crm_lead
WHERE tenant_id = T AND basic_id = LEAD_ID;
-- After Terminal A commits, expect status 4 and the winner's references.

COMMIT;
```

The application also validates that the referenced Customer and optional Contact belong to this Lead/Customer before returning the winner.

## Scenario B — Candidate conversion winner / loser

Reset a separate test Candidate to Approved (2) before this scenario.

**Terminal A**:

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;

UPDATE crm_prospecting_candidate
SET status = 4
WHERE tenant_id = T
  AND basic_id = CANDIDATE_ID
  AND status = 2;
-- Expect UPDATE 1. Keep the transaction open.

-- Simulate the application having created exactly one Lead with id NEW_LEAD_ID.
UPDATE crm_prospecting_candidate
SET status = 5, converted_lead_id = NEW_LEAD_ID
WHERE tenant_id = T
  AND basic_id = CANDIDATE_ID
  AND status = 4;
-- Expect UPDATE 1.

COMMIT;
```

**Terminal B**, while Terminal A is paused after its first UPDATE:

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;

UPDATE crm_prospecting_candidate
SET status = 4
WHERE tenant_id = T
  AND basic_id = CANDIDATE_ID
  AND status = 2;
-- Waits for Terminal A, then expect UPDATE 0.

SELECT status, converted_lead_id
FROM crm_prospecting_candidate
WHERE tenant_id = T AND basic_id = CANDIDATE_ID;
-- After Terminal A commits, expect status 5 and NEW_LEAD_ID.

COMMIT;
```

## Scenario C — claim rollback releases the row

Use a fresh test Lead (status New/Contacted/Qualified) or Candidate (status Approved).

**Terminal A**:

```sql
BEGIN;
-- Use the matching conditional UPDATE from Scenario A or B.
-- Expect UPDATE 1.
ROLLBACK;
```

After Terminal A rolls back, **Terminal B** runs the same conditional UPDATE. It should report UPDATE 1, proving the claim itself is transactional in PostgreSQL.

## What this check does not prove

- That `[UnitOfWork(true, IsolationLevel.ReadCommitted)]` is honored by the runtime.
- That the conditional UPDATE, Customer/Contact/Lead inserts, and final status update share one SqlSugar connection and transaction.
- That a thrown exception after an insert rolls back all application writes.
- That the API returns the winner's DTO under real simultaneous HTTP requests.

Those require an application-level PostgreSQL integration test using two independent DI scopes/connections and injected failure points. Keep the final Windows pull/build/database acceptance gate open until those checks are run.
