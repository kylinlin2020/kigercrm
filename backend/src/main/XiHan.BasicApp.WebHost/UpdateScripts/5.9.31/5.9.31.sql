-- 5.9.31: durable per-provider invocation attempt ledger for CRM prospecting.
-- Only tenant CRM databases with the prospecting Run table receive this operational table.
-- Fresh databases use entity CodeFirst metadata and baseline this migration version.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_provider_request
        (
            basic_id bigint NOT NULL,
            tenant_id bigint NOT NULL,
            created_time timestamp with time zone NOT NULL,
            created_id bigint NULL,
            created_by text NULL,
            run_id bigint NOT NULL,
            provider_code varchar(80) NOT NULL,
            attempt_no integer NOT NULL,
            request_key varchar(64) NOT NULL,
            state varchar(24) NOT NULL,
            started_at timestamp with time zone NOT NULL,
            completed_at timestamp with time zone NULL,
            duration_ms bigint NULL,
            http_status integer NULL,
            result_count integer NOT NULL DEFAULT 0,
            failure_code varchar(80) NULL,
            failure_summary varchar(1000) NULL,
            retry_after_at timestamp with time zone NULL,
            response_hash varchar(64) NULL,
            cache_hit boolean NOT NULL DEFAULT false,
            CONSTRAINT pk_crm_prospecting_provider_request PRIMARY KEY (basic_id),
            CONSTRAINT ck_crm_prospecting_provider_request_attempt CHECK (attempt_no > 0),
            CONSTRAINT ck_crm_prospecting_provider_request_result_count CHECK (result_count >= 0),
            CONSTRAINT ck_crm_prospecting_provider_request_duration CHECK (duration_ms IS NULL OR duration_ms >= 0),
            CONSTRAINT ck_crm_prospecting_provider_request_state CHECK (state IN ('Started','Succeeded','Failed','RateLimited','Cancelled'))
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_provider_request_te_ru_pr_at
            ON crm_prospecting_provider_request (tenant_id, run_id, provider_code, attempt_no);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_provider_request_te_pr_st
            ON crm_prospecting_provider_request (tenant_id, provider_code, started_at DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_provider_request_te_st_st
            ON crm_prospecting_provider_request (tenant_id, state, started_at DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_provider_request_te_rq_st
            ON crm_prospecting_provider_request (tenant_id, request_key, started_at DESC);
    END IF;
END
$$;
