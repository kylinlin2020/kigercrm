-- 5.9.4: enforce one sales order per source quote and one copy of each quote line per order.
-- Match the entity unique keys exactly, including IsDeleted; this also catches duplicate soft-deleted history.
-- New databases receive these constraints from entity CodeFirst metadata; this migration upgrades existing databases.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_quote'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_quote
            GROUP BY tenant_id, quote_number, version, is_deleted
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.4 blocked: duplicate quote version keys exist (including soft-deleted rows). Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_quote_teid_no_ve_isde
            ON crm_quote (tenant_id, quote_number, version, is_deleted);
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_sales_order
            WHERE source_quote_id IS NOT NULL
            GROUP BY tenant_id, source_quote_id, is_deleted
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.4 blocked: duplicate sales-order source-quote keys exist (including soft-deleted rows). Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_teid_sq_isde
            ON crm_sales_order (tenant_id, source_quote_id, is_deleted);
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order_line'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_sales_order_line
            WHERE source_quote_line_id IS NOT NULL
            GROUP BY tenant_id, sales_order_id, source_quote_line_id, is_deleted
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.4 blocked: duplicate source quote-line keys exist within a sales order (including soft-deleted rows). Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_line_teid_or_sql_isde
            ON crm_sales_order_line (tenant_id, sales_order_id, source_quote_line_id, is_deleted);
    END IF;
END
$$;
