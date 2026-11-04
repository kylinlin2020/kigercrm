-- 5.9.25: remove malformed historical email keys from duplicate lookup.
-- Keep the original email untouched. The application matcher is the authority for
-- full normalization; this migration only prevents obviously malformed values from
-- becoming high-confidence historical identity keys.
DO $$
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL THEN
        UPDATE crm_lead
        SET duplicate_email_normalized = CASE
            WHEN email IS NULL OR btrim(email) = '' THEN NULL
            WHEN btrim(email) !~ '^[^[:space:]@]+@[^[:space:]@]+$' THEN NULL
            WHEN split_part(btrim(email), '@', 2) = ''
              OR split_part(btrim(email), '@', 2) LIKE '.%'
              OR split_part(btrim(email), '@', 2) LIKE '%.'
              OR position('..' in split_part(btrim(email), '@', 2)) > 0 THEN NULL
            ELSE lower(btrim(email))
        END;
    END IF;
END
$$;
