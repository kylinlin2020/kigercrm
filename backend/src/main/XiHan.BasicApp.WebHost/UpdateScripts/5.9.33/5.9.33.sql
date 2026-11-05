-- 5.9.33: durable tenant-scoped async prospecting extraction jobs.
-- The table is added only to tenant CRM databases that already have prospecting Runs.
-- Provider submission acknowledgements are persisted as Pending, never as completed coverage.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_extraction_job
        (
            basic_id bigint NOT NULL,
            row_version bigint NOT NULL DEFAULT 0,
            tenant_id bigint NOT NULL,
            created_time timestamp with time zone NOT NULL,
            created_id bigint NULL,
            created_by text NULL,
            run_id bigint NOT NULL,
            provider_code varchar(80) NOT NULL,
            request_fingerprint varchar(64) NOT NULL,
            provider_extraction_id varchar(512) NOT NULL,
            status varchar(24) NOT NULL DEFAULT 'Pending',
            submitted_at_utc timestamp with time zone NOT NULL,
            last_polled_at_utc timestamp with time zone NULL,
            next_poll_at_utc timestamp with time zone NOT NULL,
            expires_at_utc timestamp with time zone NULL,
            poll_attempt_count integer NOT NULL DEFAULT 0,
            result_cursor varchar(2048) NULL,
            safe_metadata_json varchar(4000) NULL,
            failure_code varchar(80) NULL,
            failure_summary varchar(1000) NULL,
            result_record_count integer NOT NULL DEFAULT 0,
            lease_owner varchar(160) NULL,
            lease_token uuid NULL,
            lease_expires_at_utc timestamp with time zone NULL,
            fencing_version bigint NOT NULL DEFAULT 0,
            CONSTRAINT pk_crm_prospecting_extraction_job PRIMARY KEY (basic_id),
            CONSTRAINT ck_crm_prospecting_extraction_job_fingerprint CHECK (request_fingerprint ~ '^[0-9A-Fa-f]{64}$'),
            CONSTRAINT ck_crm_prospecting_extraction_job_poll_count CHECK (poll_attempt_count >= 0),
            CONSTRAINT ck_crm_prospecting_extraction_job_result_count CHECK (result_record_count >= 0),
            CONSTRAINT ck_crm_prospecting_extraction_job_fencing CHECK (fencing_version >= 0),
            CONSTRAINT ck_crm_prospecting_extraction_job_expiry CHECK (expires_at_utc IS NULL OR expires_at_utc > submitted_at_utc),
            CONSTRAINT ck_crm_prospecting_extraction_job_status CHECK (
                status IN ('Pending','Running','Succeeded','FetchingResults','Completed','Failed','Expired','Cancelled','NeedsReview')
            ),
            CONSTRAINT ck_crm_prospecting_extraction_job_lease CHECK (
                (lease_owner IS NULL AND lease_token IS NULL AND lease_expires_at_utc IS NULL)
                OR (lease_owner IS NOT NULL AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL)
            )
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_extraction_job_te_ru_pr_fp
            ON crm_prospecting_extraction_job (tenant_id, run_id, provider_code, request_fingerprint);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_extraction_job_te_st_np
            ON crm_prospecting_extraction_job (tenant_id, status, next_poll_at_utc);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_extraction_job_te_pr_ex
            ON crm_prospecting_extraction_job (tenant_id, provider_code, provider_extraction_id);
    END IF;
END
$$;
