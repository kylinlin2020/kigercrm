-- 5.9.15: preserve the exact campaign/strategy inputs accepted for each prospecting Run.
-- Existing runs cannot be safely reconstructed from mutable campaign/strategy rows; keep them non-replayable.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_search_objective varchar(2000) NOT NULL DEFAULT '';
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_product_scope varchar(500) NULL;
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_target_markets varchar(2000) NULL;
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_ideal_customer_profile text NULL;
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_provider_plan_json text NOT NULL DEFAULT '[]';
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS snapshot_excluded_domains_json text NULL;
    END IF;
END
$$;
