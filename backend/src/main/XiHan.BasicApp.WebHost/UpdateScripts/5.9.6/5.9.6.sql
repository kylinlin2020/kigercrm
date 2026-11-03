-- 5.9.6: upgrade tenant-scoped unique document-number indexes for legacy CRM databases.
-- Entity CodeFirst metadata already declares these indexes for new databases.
-- Include Is_Deleted to match the entity definitions and preserve the existing soft-delete uniqueness policy.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_sales_order
            GROUP BY tenant_id, order_number, is_deleted
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.6 blocked: duplicate sales-order number keys exist (including soft-deleted rows). Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_teid_no_isde
            ON crm_sales_order (tenant_id, order_number, is_deleted);
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_shipment'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_shipment
            GROUP BY tenant_id, shipment_number, is_deleted
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.6 blocked: duplicate shipment number keys exist (including soft-deleted rows). Resolve duplicates, then retry.';
        END IF;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_shipment_teid_no_isde
            ON crm_shipment (tenant_id, shipment_number, is_deleted);
    END IF;
END
$$;
