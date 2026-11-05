-- 5.9.35: retain extraction failures across result pages so a later clean final page cannot hide earlier partial failures.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_extraction_job') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_extraction_job
            ADD COLUMN IF NOT EXISTS result_failure_count integer NOT NULL DEFAULT 0;

        ALTER TABLE crm_prospecting_extraction_job
            DROP CONSTRAINT IF EXISTS ck_crm_prospecting_extraction_job_result_failure_count;
        ALTER TABLE crm_prospecting_extraction_job
            ADD CONSTRAINT ck_crm_prospecting_extraction_job_result_failure_count
            CHECK (result_failure_count >= 0);
    END IF;
END
$$;
