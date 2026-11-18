-- 5.9.46: human-gated outreach sequences, enrollments and touchpoints. No outbound send is
-- automated by the application: Planned -> Approved requires an operator, Sent requires an explicit
-- operator confirmation, and opt-outs/suppression are first-class terminal states.
CREATE TABLE IF NOT EXISTS crm_outreach_sequence
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
    name varchar(160) NOT NULL,
    steps_json text NOT NULL DEFAULT '[]',
    status integer NOT NULL DEFAULT 0,
    activated_at timestamp with time zone NULL,
    CONSTRAINT pk_crm_outreach_sequence PRIMARY KEY (basic_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_outreach_sequence_te_name_isde
    ON crm_outreach_sequence (tenant_id, name, is_deleted);

CREATE TABLE IF NOT EXISTS crm_outreach_enrollment
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
    sequence_id bigint NOT NULL,
    candidate_id bigint NOT NULL,
    status integer NOT NULL DEFAULT 0,
    current_step integer NOT NULL DEFAULT 0,
    next_action_at_utc timestamp with time zone NULL,
    suppression_reason varchar(500) NULL,
    CONSTRAINT pk_crm_outreach_enrollment PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_outreach_enrollment_te_se_st
    ON crm_outreach_enrollment (tenant_id, sequence_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_outreach_enrollment_te_ca_se_isde
    ON crm_outreach_enrollment (tenant_id, candidate_id, sequence_id, is_deleted);

CREATE TABLE IF NOT EXISTS crm_outreach_touchpoint
(
    basic_id bigint NOT NULL,
    tenant_id bigint NOT NULL,
    enrollment_id bigint NOT NULL,
    candidate_id bigint NOT NULL,
    step integer NOT NULL DEFAULT 0,
    channel integer NOT NULL DEFAULT 0,
    state integer NOT NULL DEFAULT 0,
    reply_class integer NOT NULL DEFAULT 0,
    content_hash varchar(64) NOT NULL,
    approved_by bigint NULL,
    approved_at timestamp with time zone NULL,
    occurred_at_utc timestamp with time zone NULL,
    created_id bigint NULL,
    created_time timestamp with time zone NOT NULL,
    CONSTRAINT pk_crm_outreach_touchpoint PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_outreach_touchpoint_te_en_step
    ON crm_outreach_touchpoint (tenant_id, enrollment_id, step);
CREATE INDEX IF NOT EXISTS ix_crm_outreach_touchpoint_te_ca_st
    ON crm_outreach_touchpoint (tenant_id, candidate_id, state);
