-- 5.9.3: enforce at most one primary CRM contact per tenant/customer.
-- Primary_Customer_Id is null for non-primary contacts, so multiple non-primary contacts remain valid.
-- The entity CodeFirst index creates this constraint for new databases; this migration upgrades existing databases.

ALTER TABLE IF EXISTS crm_contact
    ADD COLUMN IF NOT EXISTS primary_customer_id bigint;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_contact'
    ) THEN
        UPDATE crm_contact
        SET primary_customer_id = CASE
            WHEN is_primary THEN customer_id
            ELSE NULL
        END;

        IF EXISTS (
            SELECT 1
            FROM crm_contact
            WHERE is_primary = TRUE
            GROUP BY tenant_id, customer_id
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM contact migration 5.9.3 blocked: multiple primary contacts exist for at least one tenant/customer. Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_contact_teid_prcuid
            ON crm_contact (tenant_id, primary_customer_id);
    END IF;
END
$$;
