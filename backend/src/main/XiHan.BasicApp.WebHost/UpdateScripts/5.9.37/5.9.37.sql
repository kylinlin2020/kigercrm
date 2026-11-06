-- 5.9.37: preserve accepted quotation terms on converted sales-order snapshots.
DO $$
BEGIN
    IF to_regclass('crm_sales_order') IS NOT NULL THEN
        ALTER TABLE crm_sales_order
            ADD COLUMN IF NOT EXISTS terms_and_conditions text NULL;
    END IF;
END
$$;
