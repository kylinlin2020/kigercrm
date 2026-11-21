-- 5.9.56: exam certificates. One certificate per passed attempt; verification hash is computed
-- from immutable data and never changes, revocation/expiry are checked at verification time.
CREATE TABLE IF NOT EXISTS crm_exam_certificate
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
    certificate_no varchar(80) NOT NULL,
    attempt_id bigint NOT NULL,
    paper_id bigint NOT NULL,
    holder_user_id bigint NOT NULL,
    score integer NOT NULL,
    total_score integer NOT NULL,
    issued_at_utc timestamp with time zone NOT NULL,
    valid_until_utc timestamp with time zone NOT NULL,
    verification_hash varchar(64) NOT NULL,
    status integer NOT NULL DEFAULT 0,
    revoked_at_utc timestamp with time zone NULL,
    revoke_reason varchar(500) NULL,
    CONSTRAINT pk_crm_exam_certificate PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_exam_certificate_te_no_not_deleted
    ON crm_exam_certificate (tenant_id, certificate_no) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_crm_exam_certificate_te_us
    ON crm_exam_certificate (tenant_id, holder_user_id);
