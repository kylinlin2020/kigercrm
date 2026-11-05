-- PostgreSQL acceptance checks for migration 5.9.36.
-- Run only against a disposable/test tenant database after applying 5.9.36.sql:
--   psql "$KIGERCRM_POSTGRES_TEST_CONNECTION" -v ON_ERROR_STOP=1 -f this-file
-- All probe rows are rolled back. This validates database constraints, not application lease concurrency.
BEGIN;

DO $$
DECLARE
    probe_id bigint := -abs((extract(epoch FROM clock_timestamp()) * 1000000)::bigint);
    probe_tenant bigint := 987654321;
    duplicate_rejected boolean := false;
    invalid_state_rejected boolean := false;
    oversized_checkpoint_rejected boolean := false;
BEGIN
    INSERT INTO crm_prospecting_run_provider_execution
    (
        basic_id, row_version, tenant_id, created_time, run_id, plan_version, plan_ordinal,
        provider_code, request_fingerprint, state, attempt_count, checkpoint_bytes,
        raw_record_count, projected_record_count, failure_count
    )
    VALUES
    (
        probe_id, 0, probe_tenant, clock_timestamp(), probe_id, 3, 0,
        'probe-provider', repeat('A', 64), 'Queued', 0, 0, 0, 0, 0
    );

    BEGIN
        INSERT INTO crm_prospecting_run_provider_execution
        (
            basic_id, row_version, tenant_id, created_time, run_id, plan_version, plan_ordinal,
            provider_code, request_fingerprint, state, attempt_count, checkpoint_bytes,
            raw_record_count, projected_record_count, failure_count
        )
        VALUES
        (
            probe_id - 1, 0, probe_tenant, clock_timestamp(), probe_id, 3, 1,
            'probe-provider', repeat('B', 64), 'Queued', 0, 0, 0, 0, 0
        );
    EXCEPTION WHEN unique_violation THEN
        duplicate_rejected := true;
    END;
    IF NOT duplicate_rejected THEN
        RAISE EXCEPTION 'Provider plan unique key did not reject duplicate tenant/run/version/provider.';
    END IF;

    BEGIN
        INSERT INTO crm_prospecting_run_provider_execution
        (
            basic_id, row_version, tenant_id, created_time, run_id, plan_version, plan_ordinal,
            provider_code, request_fingerprint, state, attempt_count, checkpoint_bytes,
            raw_record_count, projected_record_count, failure_count
        )
        VALUES
        (
            probe_id - 2, 0, probe_tenant, clock_timestamp(), probe_id, 3, 2,
            'invalid-state-probe', repeat('C', 64), 'UnknownState', 0, 0, 0, 0, 0
        );
    EXCEPTION WHEN check_violation THEN
        invalid_state_rejected := true;
    END;
    IF NOT invalid_state_rejected THEN
        RAISE EXCEPTION 'Provider execution state constraint accepted an unknown state.';
    END IF;

    BEGIN
        INSERT INTO crm_prospecting_run_provider_execution
        (
            basic_id, row_version, tenant_id, created_time, run_id, plan_version, plan_ordinal,
            provider_code, request_fingerprint, state, attempt_count, checkpoint_json,
            checkpoint_hash, checkpoint_bytes, raw_record_count, projected_record_count, failure_count
        )
        VALUES
        (
            probe_id - 3, 0, probe_tenant, clock_timestamp(), probe_id, 3, 3,
            'oversized-checkpoint-probe', repeat('D', 64), 'Checkpointed', 1,
            repeat('x', 1048577), repeat('E', 64), 1048577, 0, 0, 0
        );
    EXCEPTION WHEN check_violation THEN
        oversized_checkpoint_rejected := true;
    END;
    IF NOT oversized_checkpoint_rejected THEN
        RAISE EXCEPTION 'Provider execution checkpoint byte quota was not enforced.';
    END IF;
END
$$;

ROLLBACK;
