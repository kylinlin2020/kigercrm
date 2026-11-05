-- 5.9.7: make CRM document numbers permanently unique within each tenant.
-- Soft deletion must not release a business document number.
-- Preflight all historical rows before replacing the 5.9.6 indexes.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_quote'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_quote
            GROUP BY tenant_id, quote_number, version
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.7 blocked: duplicate quotation number/version keys exist across active or soft-deleted rows. Resolve duplicates, then retry.';
        END IF;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_sales_order
            GROUP BY tenant_id, order_number
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.7 blocked: duplicate sales-order numbers exist across active or soft-deleted rows. Resolve duplicates, then retry.';
        END IF;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_shipment'
    ) THEN
        IF EXISTS (
            SELECT 1 FROM crm_shipment
            GROUP BY tenant_id, shipment_number
            HAVING COUNT(*) > 1
        ) THEN
            RAISE EXCEPTION 'CRM migration 5.9.7 blocked: duplicate shipment numbers exist across active or soft-deleted rows. Resolve duplicates, then retry.';
        END IF;
    END IF;
END
$$;

-- Replace the previous keys only after every table has passed preflight.
DROP INDEX IF EXISTS ux_crm_quote_teid_no_ve_isde;
DROP INDEX IF EXISTS ux_crm_sales_order_teid_no_isde;
DROP INDEX IF EXISTS ux_crm_shipment_teid_no_isde;

CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_quote_teid_no_ve
    ON crm_quote (tenant_id, quote_number, version);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_teid_no
    ON crm_sales_order (tenant_id, order_number);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_shipment_teid_no
    ON crm_shipment (tenant_id, shipment_number);
