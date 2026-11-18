-- 5.9.45: frozen, append-only qualification reports. Reports never overwrite candidate fields;
-- the operator decides whether a verified value becomes authoritative CRM data.
CREATE TABLE IF NOT EXISTS crm_prospecting_verification_report
(
    basic_id bigint NOT NULL,
    tenant_id bigint NOT NULL,
    candidate_id bigint NOT NULL,
    verdict integer NOT NULL DEFAULT 0,
    identity_authority varchar(160) NOT NULL DEFAULT 'none',
    snapshot_json text NOT NULL DEFAULT '{}',
    created_id bigint NULL,
    created_time timestamp with time zone NOT NULL,
    CONSTRAINT pk_crm_prospecting_verification_report PRIMARY KEY (basic_id)
);

CREATE INDEX IF NOT EXISTS ix_crm_prospecting_verification_report_te_ca_crti
    ON crm_prospecting_verification_report (tenant_id, candidate_id, created_time DESC);
