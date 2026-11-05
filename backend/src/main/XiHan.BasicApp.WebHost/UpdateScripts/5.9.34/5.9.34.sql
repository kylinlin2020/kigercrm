-- 5.9.34: durable pre-submit intent and provider idempotency key.
-- Existing acknowledged jobs remain unchanged; new Submitting rows may have no remote ID yet.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_extraction_job') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_extraction_job
            ADD COLUMN IF NOT EXISTS submission_idempotency_key uuid;

        UPDATE crm_prospecting_extraction_job
        SET submission_idempotency_key = gen_random_uuid()
        WHERE submission_idempotency_key IS NULL;

        ALTER TABLE crm_prospecting_extraction_job
            ALTER COLUMN submission_idempotency_key SET NOT NULL,
            ALTER COLUMN provider_extraction_id DROP NOT NULL,
            ALTER COLUMN submitted_at_utc DROP NOT NULL;

        ALTER TABLE crm_prospecting_extraction_job
            DROP CONSTRAINT IF EXISTS ck_crm_prospecting_extraction_job_status;
        ALTER TABLE crm_prospecting_extraction_job
            ADD CONSTRAINT ck_crm_prospecting_extraction_job_status CHECK (
                status IN ('Submitting','Pending','Running','Succeeded','FetchingResults','Completed','Failed','Expired','Cancelled','NeedsReview')
            );

        ALTER TABLE crm_prospecting_extraction_job
            DROP CONSTRAINT IF EXISTS ck_crm_prospecting_extraction_job_submission_ack;
        ALTER TABLE crm_prospecting_extraction_job
            ADD CONSTRAINT ck_crm_prospecting_extraction_job_submission_ack CHECK (
                (status = 'Submitting' AND provider_extraction_id IS NULL AND submitted_at_utc IS NULL)
                OR
                (status = 'NeedsReview' AND (
                    (provider_extraction_id IS NULL AND submitted_at_utc IS NULL)
                    OR
                    (provider_extraction_id IS NOT NULL AND length(provider_extraction_id) > 0 AND submitted_at_utc IS NOT NULL)
                ))
                OR
                (status NOT IN ('Submitting','NeedsReview') AND provider_extraction_id IS NOT NULL AND length(provider_extraction_id) > 0 AND submitted_at_utc IS NOT NULL)
            );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_extraction_job_te_submission_key
            ON crm_prospecting_extraction_job (tenant_id, submission_idempotency_key);
    END IF;
END
$$;
