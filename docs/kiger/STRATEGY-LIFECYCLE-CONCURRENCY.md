# Strategy lifecycle concurrency controls

## Framework evidence

The application references XiHan Framework 4.8.0. The matching framework source tag `v4.8.0` was inspected.

- `SqlSugarRepositoryBase.UpdateAsync(columns, predicate)` executes a conditional SQL update and returns `affectedRows > 0`. This is the compare-and-swap primitive used by the campaign and strategy repositories.
- `UnitOfWorkAttribute(true, IsolationLevel.ReadCommitted)` creates a transactional unit of work with an explicit read-committed isolation level.
- `UnitOfWorkInterceptor` creates the unit of work around the intercepted application method and completes it only after the method succeeds.
- `SqlSugarClientResolver` pins the concrete SqlSugar connection in the unit of work; subsequent repositories using the same ConfigId reuse it, so transaction begin, repository operations, commit, and rollback use the same connection.
- The framework source documents sequential access within one unit of work; concurrent operations on the shared connection are unsupported.

## Implemented strategy version allocation

Each campaign now stores a monotonic `StrategyVersionSequence`. Migration 5.9.17 backfills it to at least the maximum existing strategy version, including soft-deleted versions. Creation reads the current sequence and conditionally advances it. Competing requests using the same observed sequence cannot both advance the campaign row. A loser rereads and retries up to five times. The sequence reservation and strategy insert run in the same explicit read-committed transaction, so a failed insert rolls back the reservation.

## Implemented strategy activation

The campaign's `ActiveStrategyVersion` is the activation serialization gate. Activation conditionally updates the campaign only when its active version still equals the version observed by the caller and the campaign is not completed or archived. Once that compare-and-swap succeeds, other active timestamps are cleared and the approved target is marked active. These writes run in one explicit read-committed transaction; any failure rolls back the campaign pointer and strategy changes together.

Migration 5.9.17 aligns orphaned activation timestamps with the campaign pointer, then adds a partial unique index allowing at most one non-deleted activated strategy per tenant/campaign.

## Verification still required

Source guards were added, but no compiler or database test has been run in this environment. Required PostgreSQL tests must use independent connections and cover:

1. Simultaneous strategy creation for one campaign: distinct monotonic versions, no duplicate version, bounded retry.
2. Simultaneous activation of two approved versions: one compare-and-swap winner; final campaign pointer and active timestamp agree.
3. Forced failure after campaign pointer update: transaction rollback restores the previous pointer and activation timestamps.
4. Tenant isolation and migration against existing campaigns with active, orphaned, and soft-deleted versions.

The partial unique index migration must be exercised against a copy of representative production data before deployment. Windows pull/build/runtime acceptance remains outstanding.
