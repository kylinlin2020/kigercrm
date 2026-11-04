-- 5.9.14: persist normalized prospecting candidates separately from CRM leads.
-- Candidate records require human review; they are not automatically inserted into crm_lead.
DO $$
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL
       AND to_regclass('crm_prospecting_run') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_candidate
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
            run_id bigint NOT NULL,
            provider_code varchar(80) NOT NULL,
            external_id varchar(200) NOT NULL,
            source varchar(200) NULL,
            title varchar(300) NULL,
            company_name varchar(200) NOT NULL,
            contact_name varchar(200) NULL,
            business_email varchar(254) NULL,
            normalized_email varchar(254) NULL,
            website varchar(500) NULL,
            website_host varchar(500) NULL,
            summary text NULL,
            status integer NOT NULL DEFAULT 0,
            duplicate_lead_id bigint NULL,
            duplicate_reason varchar(500) NULL,
            converted_lead_id bigint NULL,
            CONSTRAINT pk_crm_prospecting_candidate PRIMARY KEY (basic_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_candidate_te_ru_pr_ex_is
            ON crm_prospecting_candidate (tenant_id, run_id, provider_code, external_id, is_deleted);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_candidate_te_ru_st_cr
            ON crm_prospecting_candidate (tenant_id, run_id, status, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_candidate_te_em
            ON crm_prospecting_candidate (tenant_id, normalized_email);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_candidate_te_web
            ON crm_prospecting_candidate (tenant_id, website_host);
    END IF;
END
$$;
