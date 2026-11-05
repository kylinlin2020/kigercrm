-- 5.9.9: persist a request fingerprint for server-numbered direct sales-order creation retries.
-- Existing and quotation-converted orders remain NULL and are not treated as safely replayable requests.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_sales_order'
    ) THEN
        ALTER TABLE crm_sales_order
            ADD COLUMN IF NOT EXISTS creation_request_fingerprint varchar(64) NULL;
    END IF;
END
$$;
