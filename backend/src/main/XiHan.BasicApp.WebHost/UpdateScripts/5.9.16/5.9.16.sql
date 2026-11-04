-- 5.9.16: preserve the previously persisted Converted enum value when introducing Converting.
-- Before this release, Converted = 4. New code reserves 4 for Converting and moves Converted to 5.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_candidate') IS NOT NULL THEN
        UPDATE crm_prospecting_candidate
        SET status = 5
        WHERE status = 4;
    END IF;
END
$$;
