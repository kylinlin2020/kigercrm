# Worker lease review

Branch: kiger-dev

## Findings

1. Lease expiry currently compares against timestamps supplied by the worker. Machine clock skew can cause incorrect lease acceptance or rejection. The active PostgreSQL implementation should compare expiry using database time and calculate the new expiry from that same clock.
2. Raw ADO SQL does not inherit SqlSugar query filters or the repository's strict tenant write predicate. Every raw statement must include an explicit tenant_id predicate sourced from the current AsyncLocal tenant context, and must fail closed when that context is missing.
3. The atomic claim must return the exact row from the UPDATE statement. A follow-up SELECT cannot identify which worker's token/version was granted.
4. The HostedService still uses legacy ClaimQueuedAsync and completion paths. Blanket recovery of all Running rows must be replaced before lease activation.

## Required tests

- Missing tenant context is rejected; Tenant A cannot mutate Tenant B.
- Database clock, not worker clock, controls lease expiry.
- Competing PostgreSQL connections yield exactly one claim result.
- Stale token/version cannot renew or terminate after takeover.
- Failed lease validation rolls back Candidate, Evidence and Score writes in the same transaction.

This is a review record only. It does not enable leases. No build, migration, PostgreSQL test or Windows-native test was run.

## Implementation follow-up (2026-11-04)

The six raw-SQL lease operations now fail closed when the AsyncLocal tenant context is absent. A source-level contract assertion guards against reintroducing the prior `?? 0L` fallback. This remains uncompiled and untested against PostgreSQL.
