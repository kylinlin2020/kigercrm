-- 5.9.21: enforce idempotent field-evidence identity per tenant and candidate.
-- The identity intentionally excludes retrieval time so a retry cannot duplicate the same
-- provider assertion merely because it was fetched again later.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_evidence') IS NOT NULL THEN
        IF EXISTS (
            SELECT 1
            FROM crm_prospecting_evidence
            GROUP BY tenant_id,
                     candidate_id,
                     provider_code,
                     COALESCE(provider_record_id, ''),
                     field_name,
                     source_url,
                     COALESCE(raw_payload_hash, '')
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'Cannot add prospecting evidence identity constraint: duplicate evidence rows already exist.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prosp_evidence_identity
            ON crm_prospecting_evidence (
                tenant_id,
                candidate_id,
                provider_code,
                (COALESCE(provider_record_id, '')),
                field_name,
                source_url,
                (COALESCE(raw_payload_hash, ''))
            );
    END IF;
END
$$;
