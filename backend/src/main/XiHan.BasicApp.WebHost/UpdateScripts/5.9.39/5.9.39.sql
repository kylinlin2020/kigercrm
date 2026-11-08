-- 5.9.39: create the durable, tenant-scoped Meta WhatsApp webhook inbox.
-- A webhook may only be acknowledged after its verified payload is inserted here.
-- Provider message IDs are unique per tenant and WABA to make duplicate delivery idempotent.
CREATE TABLE IF NOT EXISTS sys_whatsapp_webhook_inbox
(
    basic_id bigint NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    tenant_id bigint NOT NULL,
    created_time timestamp with time zone NOT NULL,
    created_id bigint NULL,
    created_by text NULL,
    whatsapp_business_account_id varchar(100) NOT NULL,
    phone_number_id varchar(100) NOT NULL,
    provider_message_id varchar(200) NOT NULL,
    sender_wa_id varchar(100) NOT NULL,
    provider_timestamp timestamp with time zone NOT NULL,
    payload_json text NOT NULL,
    status varchar(24) NOT NULL DEFAULT 'Pending',
    attempt_count integer NOT NULL DEFAULT 0,
    next_attempt_time timestamp with time zone NULL,
    processed_time timestamp with time zone NULL,
    last_error varchar(1000) NULL,
    CONSTRAINT pk_sys_whatsapp_webhook_inbox PRIMARY KEY (basic_id),
    CONSTRAINT ck_sys_whatsapp_webhook_inbox_attempt_count CHECK (attempt_count >= 0),
    CONSTRAINT ck_sys_whatsapp_webhook_inbox_status
        CHECK (status IN ('Pending', 'Retryable', 'Processed', 'NeedsReview'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_whatsapp_webhook_inbox_teid_waid_meid
    ON sys_whatsapp_webhook_inbox (tenant_id, whatsapp_business_account_id, provider_message_id);

CREATE INDEX IF NOT EXISTS ix_sys_whatsapp_webhook_inbox_teid_st_neat
    ON sys_whatsapp_webhook_inbox (tenant_id, status, next_attempt_time);

CREATE INDEX IF NOT EXISTS ix_sys_whatsapp_webhook_inbox_teid_crti
    ON sys_whatsapp_webhook_inbox (tenant_id, created_time DESC);

CREATE INDEX IF NOT EXISTS ix_sys_whatsapp_webhook_inbox_crid
    ON sys_whatsapp_webhook_inbox (created_id);
