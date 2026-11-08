-- 5.9.41: represent the participant identity according to WhatsApp event direction.
-- Inbound messages have a sender; outbound delivery/read receipts have a recipient.
ALTER TABLE sys_whatsapp_webhook_inbox
    ADD COLUMN IF NOT EXISTS recipient_wa_id varchar(100);

ALTER TABLE sys_whatsapp_webhook_inbox
    ALTER COLUMN sender_wa_id DROP NOT NULL;

ALTER TABLE sys_whatsapp_webhook_inbox
    DROP CONSTRAINT IF EXISTS ck_sys_whatsapp_webhook_inbox_participant;

ALTER TABLE sys_whatsapp_webhook_inbox
    ADD CONSTRAINT ck_sys_whatsapp_webhook_inbox_participant
    CHECK (NULLIF(BTRIM(sender_wa_id), '') IS NOT NULL OR NULLIF(BTRIM(recipient_wa_id), '') IS NOT NULL);
