-- 5.9.63: rendered outreach drafts awaiting explicit human approval. Nothing is sent automatically.
CREATE TABLE IF NOT EXISTS crm_outreach_draft
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
    enrollment_id bigint NOT NULL,
    candidate_id bigint NOT NULL,
    step integer NOT NULL DEFAULT 0,
    channel integer NOT NULL DEFAULT 0,
    rendered_text text NOT NULL,
    content_hash varchar(64) NOT NULL,
    status integer NOT NULL DEFAULT 0,
    approved_by bigint NULL,
    approved_at timestamp with time zone NULL,
    rejection_reason varchar(500) NULL,
    CONSTRAINT pk_crm_outreach_draft PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_outreach_draft_te_en_st
    ON crm_outreach_draft (tenant_id, enrollment_id, status);
CREATE INDEX IF NOT EXISTS ix_crm_outreach_draft_te_ca_st
    ON crm_outreach_draft (tenant_id, candidate_id, status);