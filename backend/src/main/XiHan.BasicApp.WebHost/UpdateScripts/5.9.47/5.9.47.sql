-- 5.9.47: server-owned WhatsApp account bindings. The existing webhook inbox table already has
-- the (tenant_id, waba_id, event_key) unique key that makes durable acknowledgement idempotent.
CREATE TABLE IF NOT EXISTS sys_whatsapp_channel_account
(
    basic_id bigint NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    tenant_id bigint NOT NULL,
    created_time timestamp with time zone NOT NULL,
    created_id bigint NULL,
    created_by text NULL,
    modified_time timestamp with time zone NULL,
    modified_id bigint NULL,
    modified_by text NULL,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_time timestamp with time zone NULL,
    deleted_id bigint NULL,
    deleted_by text NULL,
    display_name varchar(160) NOT NULL,
    whatsapp_business_account_id varchar(100) NOT NULL,
    phone_number_id varchar(100) NOT NULL,
    credential_ref varchar(300) NULL,
    status integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_sys_whatsapp_channel_account PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_whatsapp_channel_account_te_wa_ph_isde
    ON sys_whatsapp_channel_account (tenant_id, whatsapp_business_account_id, phone_number_id, is_deleted);
CREATE INDEX IF NOT EXISTS ix_sys_whatsapp_channel_account_te_st
    ON sys_whatsapp_channel_account (tenant_id, status);
