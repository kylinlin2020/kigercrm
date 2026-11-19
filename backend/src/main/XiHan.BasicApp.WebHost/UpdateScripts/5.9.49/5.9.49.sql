-- 5.9.49: WeCom tenant bindings and command-gateway audit. Encrypted callbacks stay fail-closed
-- until an explicit decryption key is configured; no external send is automated here.
CREATE TABLE IF NOT EXISTS sys_wecom_account_binding
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
    corp_id varchar(120) NOT NULL,
    agent_id varchar(120) NOT NULL,
    credential_ref varchar(300) NULL,
    status integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_sys_wecom_account_binding PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_wecom_account_binding_te_corp_agent_isde
    ON sys_wecom_account_binding (tenant_id, corp_id, agent_id, is_deleted);

CREATE TABLE IF NOT EXISTS sys_wecom_command_audit
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
    corp_id varchar(120) NOT NULL,
    agent_id varchar(120) NOT NULL,
    from_user_id varchar(200) NOT NULL,
    provider_message_id varchar(200) NOT NULL,
    command varchar(80) NOT NULL,
    outcome integer NOT NULL DEFAULT 0,
    reason varchar(300) NULL,
    reply_hash varchar(64) NOT NULL DEFAULT '',
    CONSTRAINT pk_sys_wecom_command_audit PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_wecom_command_audit_te_msg_isde
    ON sys_wecom_command_audit (tenant_id, provider_message_id, is_deleted);
CREATE INDEX IF NOT EXISTS ix_sys_wecom_command_audit_te_from_time
    ON sys_wecom_command_audit (tenant_id, from_user_id, created_time DESC);
