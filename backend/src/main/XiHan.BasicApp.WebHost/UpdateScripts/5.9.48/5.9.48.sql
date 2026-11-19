-- 5.9.48: staged chat history import with auditable per-row verdicts. Imported rows are review
-- records, not auto-projected into conversations; the unique key prevents replaying one file twice.
CREATE TABLE IF NOT EXISTS sys_chat_history_import_batch
(
    basic_id bigint NOT NULL,
    tenant_id bigint NOT NULL,
    file_name varchar(260) NOT NULL,
    source_channel varchar(40) NOT NULL,
    total_count integer NOT NULL DEFAULT 0,
    imported_count integer NOT NULL DEFAULT 0,
    duplicate_count integer NOT NULL DEFAULT 0,
    invalid_count integer NOT NULL DEFAULT 0,
    status integer NOT NULL DEFAULT 0,
    created_id bigint NULL,
    created_time timestamp with time zone NOT NULL,
    CONSTRAINT pk_sys_chat_history_import_batch PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_sys_chat_history_import_batch_te_crti
    ON sys_chat_history_import_batch (tenant_id, created_time DESC);

CREATE TABLE IF NOT EXISTS sys_chat_history_import_row
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
    batch_id bigint NOT NULL,
    source_channel varchar(40) NOT NULL,
    external_id varchar(200) NOT NULL,
    sender varchar(200) NOT NULL,
    recipient varchar(200) NOT NULL,
    content_hash varchar(64) NOT NULL,
    sent_at_utc timestamp with time zone NOT NULL,
    status integer NOT NULL DEFAULT 0,
    reason varchar(300) NULL,
    CONSTRAINT pk_sys_chat_history_import_row PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_sys_chat_history_import_row_te_ba_st
    ON sys_chat_history_import_row (tenant_id, batch_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sys_chat_history_import_row_te_ba_ex_isde
    ON sys_chat_history_import_row (tenant_id, batch_id, external_id, is_deleted);
