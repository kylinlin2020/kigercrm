-- 5.9.35: allow NeedsReview to preserve a known remote extraction identity.
-- A review state can represent either an uncertain submission (no remote ID) or
-- an unrecognized/ambiguous remote status after a provider job was acknowledged.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_extraction_job') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_extraction_job
            DROP CONSTRAINT IF EXISTS ck_crm_prospecting_extraction_job_submission_ack;

        ALTER TABLE crm_prospecting_extraction_job
            ADD CONSTRAINT ck_crm_prospecting_extraction_job_submission_ack CHECK (
                (
                    status = 'Submitting'
                    AND provider_extraction_id IS NULL
                    AND submitted_at_utc IS NULL
                )
                OR
                (
                    status = 'NeedsReview'
                    AND (
                        (
                            provider_extraction_id IS NULL
                            AND submitted_at_utc IS NULL
                        )
                        OR
                        (
                            provider_extraction_id IS NOT NULL
                            AND length(provider_extraction_id) > 0
                            AND submitted_at_utc IS NOT NULL
                        )
                    )
                )
                OR
                (
                    status NOT IN ('Submitting', 'NeedsReview')
                    AND provider_extraction_id IS NOT NULL
                    AND length(provider_extraction_id) > 0
                    AND submitted_at_utc IS NOT NULL
                )
            );
    END IF;
END
$$;
