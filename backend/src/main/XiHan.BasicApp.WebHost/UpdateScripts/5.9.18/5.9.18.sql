-- 5.9.18: persist immutable field-level provenance for prospecting candidates.
-- Evidence is tenant-scoped and stores hashes rather than copying potentially sensitive field values.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_candidate') IS NOT NULL THEN
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prosp_candidate_tenant_basic
            ON crm_prospecting_candidate (tenant_id, basic_id);

        CREATE TABLE IF NOT EXISTS crm_prospecting_evidence (
            basic_id bigint PRIMARY KEY,
            tenant_id bigint NOT NULL,
            candidate_id bigint NOT NULL,
            provider_code varchar(80) NOT NULL,
            provider_record_id varchar(240) NULL,
            field_name varchar(100) NOT NULL,
            field_value_hash varchar(64) NULL,
            source_url varchar(2000) NOT NULL,
            evidence_type varchar(40) NOT NULL DEFAULT 'direct',
            verification_status varchar(40) NOT NULL DEFAULT 'unverified',
            evidence_summary varchar(1000) NULL,
            published_at_utc timestamptz NULL,
            retrieved_at_utc timestamptz NOT NULL,
            raw_payload_hash varchar(64) NULL,
            created_id bigint NULL,
            created_time timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT fk_crm_prospecting_evidence_candidate
                FOREIGN KEY (tenant_id, candidate_id) REFERENCES crm_prospecting_candidate (tenant_id, basic_id)
        );

        CREATE INDEX IF NOT EXISTS ix_crm_prosp_evidence_tenant_candidate_field
            ON crm_prospecting_evidence (tenant_id, candidate_id, field_name);

        CREATE INDEX IF NOT EXISTS ix_crm_prosp_evidence_tenant_provider_retrieved
            ON crm_prospecting_evidence (tenant_id, provider_code, retrieved_at_utc DESC);
    END IF;
END
$$;
