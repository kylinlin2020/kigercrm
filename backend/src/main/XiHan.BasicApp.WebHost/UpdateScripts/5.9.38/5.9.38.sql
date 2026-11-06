-- 5.9.38: preserve contact and opportunity names on quotation and sales-order snapshots.
DO $$
BEGIN
    IF to_regclass('crm_quote') IS NOT NULL THEN
        ALTER TABLE crm_quote
            ADD COLUMN IF NOT EXISTS opportunity_name_snapshot varchar(200) NULL;
    END IF;

    IF to_regclass('crm_sales_order') IS NOT NULL THEN
        ALTER TABLE crm_sales_order
            ADD COLUMN IF NOT EXISTS contact_name_snapshot varchar(200) NULL,
            ADD COLUMN IF NOT EXISTS opportunity_name_snapshot varchar(200) NULL;
    END IF;
END
$$;
