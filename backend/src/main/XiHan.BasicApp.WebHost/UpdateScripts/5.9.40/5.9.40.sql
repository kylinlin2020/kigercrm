-- 5.9.40: change WhatsApp inbox idempotency from message-level to event-level.
-- A single wamid can have multiple legitimate status transitions. Existing rows are
-- backfilled as InboundMessage events before the old message-only unique index is removed.
ALTER TABLE sys_whatsapp_webhook_inbox
    ADD COLUMN IF NOT EXISTS event_key varchar(64),
    ADD COLUMN IF NOT EXISTS event_type varchar(32);

UPDATE sys_whatsapp_webhook_inbox
SET event_key = md5(
        tenant_id::text || ':' ||
        whatsapp_business_account_id || ':' ||
        provider_message_id
    ),
    event_type = 'InboundMessage'
WHERE event_key IS NULL OR event_type IS NULL;

ALTER TABLE sys_whatsapp_webhook_inbox
    ALTER COLUMN event_key SET NOT NULL,
    ALTER COLUMN event_type SET NOT NULL;

DROP INDEX IF EXISTS ux_sys_whatsapp_webhook_inbox_teid_waid_meid;

CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_whatsapp_webhook_inbox_teid_waid_evke
    ON sys_whatsapp_webhook_inbox (tenant_id, whatsapp_business_account_id, event_key);
