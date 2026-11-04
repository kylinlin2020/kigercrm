-- 5.9.27: tighten the historical website-key allowlist.
-- 5.9.26 accepted any 1-5 digit port, including values rejected by System.Uri.
-- Rebuild without port-bearing URLs so invalid/out-of-range ports cannot create
-- duplicate keys that the application normalizer would reject.
DO $$
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL THEN
        UPDATE crm_lead
        SET website_host = CASE
            WHEN website IS NULL OR btrim(website) = '' THEN NULL
            WHEN lower(btrim(website)) !~ '^(https?://)?(www\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?([/?#].*)?$' THEN NULL
            ELSE NULLIF(
                regexp_replace(
                    regexp_replace(lower(btrim(website)), '^(https?://)?(www\.)?', ''),
                    '[/?#].*$',
                    ''
                ),
                ''
            )
        END;
    END IF;
END
$$;
