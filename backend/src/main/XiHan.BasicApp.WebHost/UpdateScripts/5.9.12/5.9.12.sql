-- 5.9.12: persist the request fingerprint used for prospecting-run idempotency.
-- New databases receive this column from entity CodeFirst; existing CRM tenant databases are upgraded here.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS payload_hash varchar(64) NOT NULL DEFAULT '';
    END IF;
END
$$;
