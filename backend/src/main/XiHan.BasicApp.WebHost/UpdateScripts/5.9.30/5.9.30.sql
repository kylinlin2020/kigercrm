-- 5.9.30: additive lease/fencing metadata for prospecting Runs.
-- This migration deliberately does not activate lease claims or change recovery behavior.
-- Existing Running rows retain NULL lease metadata and remain under the single-worker recovery policy.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_run') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_run
            ADD COLUMN IF NOT EXISTS lease_owner varchar(160) NULL,
            ADD COLUMN IF NOT EXISTS lease_token uuid NULL,
            ADD COLUMN IF NOT EXISTS lease_expires_at timestamp with time zone NULL,
            ADD COLUMN IF NOT EXISTS lease_heartbeat_at timestamp with time zone NULL,
            ADD COLUMN IF NOT EXISTS fencing_version bigint NOT NULL DEFAULT 0;

        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_te_st_leaseexp
            ON crm_prospecting_run (tenant_id, status, lease_expires_at);
    END IF;
END
$$;
