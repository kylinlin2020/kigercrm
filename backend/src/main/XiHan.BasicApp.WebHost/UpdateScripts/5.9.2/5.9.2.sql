-- 5.9.2: normalized CRM lead identity keys for duplicate candidate lookup.
-- Safe for platform and tenant databases. The CRM lead table is tenant-scoped.
-- No unique constraints are created: duplicate detection is advisory only.

ALTER TABLE IF EXISTS crm_lead
    ADD COLUMN IF NOT EXISTS duplicate_email_normalized varchar(254),
    ADD COLUMN IF NOT EXISTS phone_digits varchar(40),
    ADD COLUMN IF NOT EXISTS website_host varchar(500);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = current_schema()
          AND table_name = 'crm_lead'
    ) THEN
        UPDATE crm_lead
        SET duplicate_email_normalized = NULLIF(lower(btrim(email)), ''),
            phone_digits = NULLIF(regexp_replace(coalesce(phone, ''), '[^0-9]', '', 'g'), ''),
            website_host = NULLIF(
                regexp_replace(
                    regexp_replace(
                        regexp_replace(
                            split_part(
                                split_part(
                                    regexp_replace(lower(btrim(coalesce(website, ''))), '^[a-z][a-z0-9+.-]*://', ''),
                                    '/',
                                    1
                                ),
                                '?',
                                1
                            ),
                            '^www\\.',
                            ''
                        ),
                        ':[0-9]+$',
                        ''
                    ),
                    '\\.$',
                    ''
                ),
                ''
            );
    END IF;
END
$$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = current_schema()
          AND table_name = 'crm_lead'
    ) THEN
        CREATE INDEX IF NOT EXISTS ix_crm_lead_tenant_duplicate_email_normalized
            ON crm_lead (tenant_id, duplicate_email_normalized);
        CREATE INDEX IF NOT EXISTS ix_crm_lead_tenant_phone_digits
            ON crm_lead (tenant_id, phone_digits);
        CREATE INDEX IF NOT EXISTS ix_crm_lead_tenant_website_host
            ON crm_lead (tenant_id, website_host);
    END IF;
END
$$;
