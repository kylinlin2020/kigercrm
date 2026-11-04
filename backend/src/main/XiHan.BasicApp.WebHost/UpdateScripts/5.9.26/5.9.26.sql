-- 5.9.26: rebuild historical website identity keys using a conservative ASCII DNS allowlist.
-- The application uses Uri.IdnHost and validates HTTP(S), DNS hosts, and userinfo.
-- PostgreSQL core has no guaranteed IDNA conversion facility, so non-ASCII/IDN
-- values are deliberately left unindexed rather than creating non-equivalent keys.
DO $$
DECLARE
    raw_website text;
    authority text;
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL THEN
        UPDATE crm_lead
        SET website_host = CASE
            WHEN website IS NULL OR btrim(website) = '' THEN NULL
            WHEN lower(btrim(website)) !~ '^(https?://)?(www\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?(:[0-9]{1,5})?([/?#].*)?$' THEN NULL
            ELSE NULLIF(
                regexp_replace(
                    regexp_replace(lower(btrim(website)), '^(https?://)?(www\.)?', ''),
                    '[:/?#].*$',
                    ''
                ),
                ''
            )
        END;
    END IF;
END
$$;
