-- 5.9.62: local customer background research. Facts are structured tokens rendered by client i18n;
-- reports are superseded on regeneration so research history stays auditable. No external calls.
CREATE TABLE IF NOT EXISTS crm_background_research_report
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
    generated_at_utc timestamp with time zone NOT NULL,
    generated_by_user_id bigint NULL,
    evidence_count integer NOT NULL DEFAULT 0,
    opportunity_count integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_crm_background_research_report PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_background_research_report_te_cu_st
    ON crm_background_research_report (tenant_id, customer_id, status);

CREATE TABLE IF NOT EXISTS crm_research_evidence
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
    report_id bigint NOT NULL,
    source integer NOT NULL,
    fact_key varchar(80) NOT NULL,
    fact_value varchar(500) NOT NULL,
    source_ref varchar(200) NULL,
    confidence integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_crm_research_evidence PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_research_evidence_te_re_so
    ON crm_research_evidence (tenant_id, report_id, source);

CREATE TABLE IF NOT EXISTS crm_market_opportunity
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
    report_id bigint NOT NULL,
    customer_id bigint NOT NULL,
    title varchar(200) NOT NULL,
    fact_key varchar(80) NOT NULL,
    fact_value varchar(500) NOT NULL,
    priority_score integer NOT NULL DEFAULT 0,
    status integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_crm_market_opportunity PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_market_opportunity_te_re_st
    ON crm_market_opportunity (tenant_id, report_id, status);