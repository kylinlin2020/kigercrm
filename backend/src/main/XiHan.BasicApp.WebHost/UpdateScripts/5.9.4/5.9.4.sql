-- 5.9.4: enforce one sales order per source quote and one copy of each quote line per order.
-- New databases receive these constraints from entity CodeFirst metadata; this migration upgrades existing databases.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_sales_order
            WHERE source_quote_id IS NOT NULL AND is_deleted = FALSE
            GROUP BY tenant_id, source_quote_id
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.4 blocked: multiple active sales orders reference the same source quote. Resolve duplicates, then retry.';
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
            WHERE source_quote_line_id IS NOT NULL AND is_deleted = FALSE
            GROUP BY tenant_id, sales_order_id, source_quote_line_id
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.4 blocked: duplicate active copies of a quote line exist within a sales order. Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_line_teid_or_sql_isde
            ON crm_sales_order_line (tenant_id, sales_order_id, source_quote_line_id, is_deleted);
    END IF;
END
$$;
