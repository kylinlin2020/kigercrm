-- 5.9.13: persist the immutable raw provider response snapshot for each prospecting Run.
-- New databases receive this nullable text column from entity CodeFirst.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS raw_results_json text NULL;
    END IF;
END
$$;
