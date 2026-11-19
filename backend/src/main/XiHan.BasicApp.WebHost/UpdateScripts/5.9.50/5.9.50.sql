-- 5.9.50: tenant-owned Baileys session contracts. No real protocol client or QR payload is stored;
-- an operator explicitly confirms connection state transitions. Device logout is a hard stop.
CREATE TABLE IF NOT EXISTS sys_baileys_session
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
    auth_state_ref varchar(300) NULL,
    status integer NOT NULL DEFAULT 0,
    health integer NOT NULL DEFAULT 0,
    qr_issued_at_utc timestamp with time zone NULL,
    last_connected_at_utc timestamp with time zone NULL,
    note varchar(500) NULL,
    CONSTRAINT pk_sys_baileys_session PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_sys_baileys_session_te_st_health
    ON sys_baileys_session (tenant_id, status, health);
