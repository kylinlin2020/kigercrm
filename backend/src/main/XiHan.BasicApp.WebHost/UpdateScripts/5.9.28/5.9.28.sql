-- 5.9.28: enforce at most one converted Customer per Lead and tenant.
DO $$
BEGIN
    IF to_regclass('crm_customer') IS NOT NULL THEN
        ALTER TABLE crm_customer
            ADD COLUMN IF NOT EXISTS converted_lead_id bigint NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_customer_tenant_converted_lead
            ON crm_customer (tenant_id, converted_lead_id);
    END IF;
END
$$;
