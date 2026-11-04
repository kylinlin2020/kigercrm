-- 5.9.10: create the permanent tenant-scoped CRM document request ledger.
-- The table is creation-only (no Is_Deleted column), so a request key is never released by soft deletion.

DO $$
BEGIN
    IF to_regclass('crm_create_request') IS NULL THEN
        CREATE TABLE crm_create_request
        (
            basic_id bigint NOT NULL,
            row_version bigint NOT NULL DEFAULT 0,
            tenant_id bigint NOT NULL,
            created_time timestamp with time zone NOT NULL,
            created_id bigint NULL,
            created_by text NULL,
            document_type varchar(32) NOT NULL,
            request_key varchar(100) NOT NULL,
            payload_hash varchar(64) NOT NULL,
            document_id bigint NULL,
            document_number varchar(40) NULL,
            CONSTRAINT pk_crm_create_request PRIMARY KEY (basic_id)
        );
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_crm_create_request_teid_crti
    ON crm_create_request (tenant_id, created_time DESC);

CREATE INDEX IF NOT EXISTS ix_crm_create_request_crid
    ON crm_create_request (created_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_create_request_teid_dt_rqke
    ON crm_create_request (tenant_id, document_type, request_key);

CREATE INDEX IF NOT EXISTS ix_crm_create_request_teid_dcid
    ON crm_create_request (tenant_id, document_id);
