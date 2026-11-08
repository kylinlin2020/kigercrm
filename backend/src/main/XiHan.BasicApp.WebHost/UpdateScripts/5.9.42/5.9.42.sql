-- 5.9.42: add durable consumer leases and fencing to the WhatsApp webhook inbox.
-- Reclaimed events increment Fencing_Version; stale consumers cannot acknowledge or retry them.
ALTER TABLE sys_whatsapp_webhook_inbox
    ADD COLUMN IF NOT EXISTS lease_owner varchar(160),
    ADD COLUMN IF NOT EXISTS lease_token uuid,
    ADD COLUMN IF NOT EXISTS lease_expires_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS fencing_version bigint NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_sys_whatsapp_webhook_inbox_teid_st_neat_lease
    ON sys_whatsapp_webhook_inbox
       (tenant_id, status, next_attempt_time, lease_expires_at);
