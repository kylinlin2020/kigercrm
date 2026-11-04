-- 5.9.32: durable campaign/provider coverage checkpoints.
-- Coverage is advanced only by the same statement that finalizes a complete successful request.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL
       AND to_regclass('crm_prospecting_provider_request') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_provider_coverage
        (
            tenant_id bigint NOT NULL,
            campaign_id bigint NOT NULL,
            provider_code varchar(80) NOT NULL,
            coverage_scope_hash varchar(64) NOT NULL,
            last_successful_scan_at timestamp with time zone NOT NULL,
            last_run_id bigint NOT NULL,
            last_result_count integer NOT NULL DEFAULT 0,
            last_response_hash varchar(64) NULL,
            version bigint NOT NULL DEFAULT 1,
            CONSTRAINT pk_crm_prospecting_provider_coverage
                PRIMARY KEY (tenant_id, campaign_id, provider_code, coverage_scope_hash),
            CONSTRAINT ck_crm_prospecting_provider_coverage_result_count
                CHECK (last_result_count >= 0),
            CONSTRAINT ck_crm_prospecting_provider_coverage_version
                CHECK (version > 0),
            CONSTRAINT ck_crm_prospecting_provider_coverage_scope_hash
                CHECK (coverage_scope_hash ~ '^[0-9A-Fa-f]{64}$'),
            CONSTRAINT ck_crm_prospecting_provider_coverage_response_hash
                CHECK (last_response_hash IS NULL OR last_response_hash ~ '^[0-9A-Fa-f]{64}$')
        );

        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_provider_coverage_te_ca_time
            ON crm_prospecting_provider_coverage (tenant_id, campaign_id, last_successful_scan_at DESC);
    END IF;
END
$$;
