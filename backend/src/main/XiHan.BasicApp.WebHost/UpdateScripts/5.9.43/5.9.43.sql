-- 5.9.43: durable Super Search missions. The frozen plan is the audit record; execution still goes
-- through the approved Run pipeline, so no provider work can bypass strategy approval.
CREATE TABLE IF NOT EXISTS crm_prospecting_supersearch_mission
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
    campaign_id bigint NOT NULL,
    name varchar(160) NOT NULL,
    request_key varchar(100) NOT NULL,
    scope_fingerprint varchar(64) NOT NULL,
    depth integer NOT NULL DEFAULT 0,
    planned_rounds integer NOT NULL DEFAULT 0,
    provider_count integer NOT NULL DEFAULT 0,
    provider_plan_json text NOT NULL DEFAULT '[]',
    plan_snapshot_json text NOT NULL DEFAULT '{}',
    status integer NOT NULL DEFAULT 0,
    run_id bigint NULL,
    CONSTRAINT pk_crm_prospecting_supersearch_mission PRIMARY KEY (basic_id)
);

CREATE INDEX IF NOT EXISTS ix_crm_prospecting_supersearch_mission_te_ca_crti
    ON crm_prospecting_supersearch_mission (tenant_id, campaign_id, created_time DESC);

-- Soft-deleted rows keep the key reserved: a deleted mission cannot free a RequestKey for a
-- different plan, mirroring the Run idempotency contract.
CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_supersearch_mission_te_rq_isde
    ON crm_prospecting_supersearch_mission (tenant_id, request_key, is_deleted);
