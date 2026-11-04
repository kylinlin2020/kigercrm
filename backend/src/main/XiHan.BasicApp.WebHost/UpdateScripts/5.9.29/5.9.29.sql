-- 5.9.29: make the worker's first score snapshot idempotent per tenant/candidate/scorecard version.
-- Nullable keys preserve the append-only semantics of ordinary historical score snapshots.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_score') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_score
            ADD COLUMN IF NOT EXISTS idempotency_key varchar(160) NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_score_te_idemkey
            ON crm_prospecting_score (tenant_id, idempotency_key);
    END IF;
END
$$;
