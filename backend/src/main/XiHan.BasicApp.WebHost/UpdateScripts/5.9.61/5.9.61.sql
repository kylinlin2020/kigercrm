-- 5.9.61: tenant customer pool. Claim is exclusive and audited; idle claims are recoverable.
CREATE TABLE IF NOT EXISTS crm_customer_pool_entry
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
    customer_id bigint NOT NULL,
    status integer NOT NULL DEFAULT 0,
    claimed_user_id bigint NULL,
    claimed_at_utc timestamp with time zone NULL,
    claim_count integer NOT NULL DEFAULT 0,
    last_released_at_utc timestamp with time zone NULL,
    note varchar(500) NULL,
    CONSTRAINT pk_crm_customer_pool_entry PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_customer_pool_entry_te_cu_not_deleted
    ON crm_customer_pool_entry (tenant_id, customer_id) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_crm_customer_pool_entry_te_st_claim
    ON crm_customer_pool_entry (tenant_id, status, claimed_at_utc);