-- 5.9.8: persist a request fingerprint for server-numbered shipment creation retries.
-- Existing shipments remain NULL and are never treated as safely replayable requests.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'crm_shipment'
    ) THEN
        ALTER TABLE crm_shipment
            ADD COLUMN IF NOT EXISTS creation_request_fingerprint varchar(64) NULL;
    END IF;
END
$$;
