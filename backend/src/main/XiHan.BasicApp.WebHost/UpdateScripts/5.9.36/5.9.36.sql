-- 5.9.36: durable per-provider execution checkpoints for prospecting Runs.
-- This is separate from the invocation-attempt ledger and does not enable replay by itself.
-- Only tenant CRM databases with the prospecting Run table receive this operational table.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_run_provider_execution
        (
            basic_id bigint NOT NULL,
            row_version bigint NOT NULL DEFAULT 0,
            tenant_id bigint NOT NULL,
            created_time timestamp with time zone NOT NULL,
            created_id bigint NULL,
            created_by text NULL,
            run_id bigint NOT NULL,
            plan_version integer NOT NULL,
            plan_ordinal integer NOT NULL,
            provider_code varchar(80) NOT NULL,
            request_fingerprint varchar(64) NOT NULL,
            state varchar(24) NOT NULL DEFAULT 'Queued',
            attempt_count integer NOT NULL DEFAULT 0,
            checkpoint_json text NULL,
            checkpoint_hash varchar(64) NULL,
            checkpoint_bytes integer NOT NULL DEFAULT 0,
            raw_record_count integer NOT NULL DEFAULT 0,
            projected_record_count integer NOT NULL DEFAULT 0,
            failure_count integer NOT NULL DEFAULT 0,
            started_at timestamp with time zone NULL,
            completed_at timestamp with time zone NULL,
            failure_code varchar(80) NULL,
            failure_summary varchar(1000) NULL,
            CONSTRAINT pk_crm_prospecting_run_provider_execution PRIMARY KEY (basic_id),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_plan CHECK (plan_version > 0 AND plan_ordinal >= 0),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_fingerprint CHECK (request_fingerprint ~ '^[0-9A-Fa-f]{64}$'),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_checkpoint_hash CHECK (checkpoint_hash IS NULL OR checkpoint_hash ~ '^[0-9A-Fa-f]{64}$'),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_counts CHECK (
                attempt_count >= 0 AND checkpoint_bytes >= 0 AND raw_record_count >= 0
                AND projected_record_count >= 0 AND failure_count >= 0
            ),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_state CHECK (
                state IN ('Queued','Started','Checkpointed','Projecting','Succeeded','Failed','NeedsReview','Skipped')
            ),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_completion CHECK (
                (state IN ('Succeeded','Failed','NeedsReview','Skipped') AND completed_at IS NOT NULL)
                OR (state NOT IN ('Succeeded','Failed','NeedsReview','Skipped') AND completed_at IS NULL)
            ),
            CONSTRAINT ck_crm_prospecting_run_provider_execution_checkpoint_shape CHECK (
                (checkpoint_json IS NULL AND checkpoint_hash IS NULL AND checkpoint_bytes = 0)
                OR (checkpoint_json IS NOT NULL AND checkpoint_hash IS NOT NULL
                    AND checkpoint_bytes BETWEEN 1 AND 1048576
                    AND checkpoint_bytes = octet_length(checkpoint_json))
            )
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_run_provider_execution_te_ru_pl_pr
            ON crm_prospecting_run_provider_execution (tenant_id, run_id, plan_version, provider_code);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_provider_execution_te_ru_st
            ON crm_prospecting_run_provider_execution (tenant_id, run_id, state);
    END IF;
END
$$;
