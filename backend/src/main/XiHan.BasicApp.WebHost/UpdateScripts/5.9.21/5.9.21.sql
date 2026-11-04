-- 5.9.21: align retry-safe evidence identity across CodeFirst and upgraded tenant databases.
-- Normalize nullable identity fields so ordinary composite unique indexes have identical semantics
-- in fresh tenants and upgraded tenants. Index the SHA-256 URL fingerprint rather than a 2000-byte
-- URL value to stay within PostgreSQL B-tree index tuple limits.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_evidence') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_evidence
            ADD COLUMN IF NOT EXISTS source_url_hash varchar(32) NOT NULL DEFAULT '';

        UPDATE crm_prospecting_evidence
           SET provider_record_id = COALESCE(provider_record_id, ''),
               raw_payload_hash = COALESCE(raw_payload_hash, ''),
               source_url_hash = md5(source_url)
         WHERE provider_record_id IS NULL
            OR raw_payload_hash IS NULL
            OR source_url_hash = '';

        ALTER TABLE crm_prospecting_evidence
            ALTER COLUMN provider_record_id SET DEFAULT '',
            ALTER COLUMN provider_record_id SET NOT NULL,
            ALTER COLUMN raw_payload_hash SET DEFAULT '',
            ALTER COLUMN raw_payload_hash SET NOT NULL;

        IF EXISTS (
            SELECT 1
              FROM crm_prospecting_evidence
             GROUP BY tenant_id, candidate_id, provider_code, provider_record_id,
                      field_name, source_url_hash, raw_payload_hash
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'Cannot add prospecting evidence identity constraint: duplicate evidence rows already exist.';
        END IF;

        DROP INDEX IF EXISTS ux_crm_prosp_evidence_identity;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prosp_evidence_identity
            ON crm_prospecting_evidence (
                tenant_id, candidate_id, provider_code, provider_record_id,
                field_name, source_url_hash, raw_payload_hash
            );
    END IF;
END
$$;
