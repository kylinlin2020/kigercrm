-- 5.9.11: initial tenant-scoped CRM prospecting campaign, strategy-version and run tables.
-- Existing tenant databases receive these tables here. Fresh databases are created from entity CodeFirst metadata
-- and baseline the current migration version, so this script is intentionally safe when the tables already exist.
-- All identifiers are lowercase to match PostgreSQL's unquoted SqlSugar CodeFirst naming.

DO $$
BEGIN
    IF to_regclass('crm_lead') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_campaign
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
            objective varchar(1000) NULL,
            product_scope varchar(500) NULL,
            target_markets text NULL,
            ideal_customer_profile text NULL,
            status integer NOT NULL DEFAULT 0,
            owner_user_id bigint NULL,
            active_strategy_version integer NULL,
            CONSTRAINT pk_crm_prospecting_campaign PRIMARY KEY (basic_id)
        );
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_campaign_teid_crti
            ON crm_prospecting_campaign (tenant_id, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_campaign_crid
            ON crm_prospecting_campaign (created_id);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_campaign_teid_isde
            ON crm_prospecting_campaign (tenant_id, is_deleted);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_campaign_teid_st_cr
            ON crm_prospecting_campaign (tenant_id, status, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_campaign_teid_ow
            ON crm_prospecting_campaign (tenant_id, owner_user_id);

        CREATE TABLE IF NOT EXISTS crm_prospecting_strategy
        (
            basic_id bigint NOT NULL,
            row_version bigint NOT NULL DEFAULT 0,
            tenant_id bigint NOT NULL,
            created_time timestamp with time zone NOT NULL,
            created_id bigint NULL,
            created_by text NULL,
            campaign_id bigint NOT NULL,
            version integer NOT NULL,
            name varchar(160) NOT NULL,
            search_objective text NOT NULL,
            provider_plan_json text NOT NULL DEFAULT '[]',
            excluded_domains_json text NULL,
            approval_status integer NOT NULL DEFAULT 0,
            approved_by bigint NULL,
            approved_at timestamp with time zone NULL,
            activated_at timestamp with time zone NULL,
            CONSTRAINT pk_crm_prospecting_strategy PRIMARY KEY (basic_id)
        );
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_strategy_teid_crti
            ON crm_prospecting_strategy (tenant_id, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_strategy_crid
            ON crm_prospecting_strategy (created_id);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_strategy_teid_isde
            ON crm_prospecting_strategy (tenant_id, is_deleted);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_strategy_te_ca_ve_isde
            ON crm_prospecting_strategy (tenant_id, campaign_id, version, is_deleted);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_strategy_te_ca_st
            ON crm_prospecting_strategy (tenant_id, campaign_id, approval_status);

        CREATE TABLE IF NOT EXISTS crm_prospecting_run
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
            strategy_id bigint NOT NULL,
            strategy_version integer NOT NULL,
            request_key varchar(100) NOT NULL,
            status integer NOT NULL DEFAULT 0,
            started_at timestamp with time zone NULL,
            completed_at timestamp with time zone NULL,
            raw_record_count integer NOT NULL DEFAULT 0,
            candidate_count integer NOT NULL DEFAULT 0,
            failure_count integer NOT NULL DEFAULT 0,
            failure_summary varchar(2000) NULL,
            CONSTRAINT pk_crm_prospecting_run PRIMARY KEY (basic_id)
        );
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_teid_crti
            ON crm_prospecting_run (tenant_id, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_crid
            ON crm_prospecting_run (created_id);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_teid_isde
            ON crm_prospecting_run (tenant_id, is_deleted);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_teid_crti
            ON crm_prospecting_run (tenant_id, created_time DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_crid
            ON crm_prospecting_run (created_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_run_te_rq
            ON crm_prospecting_run (tenant_id, request_key);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_run_te_ca_st_cr
            ON crm_prospecting_run (tenant_id, campaign_id, status, created_time DESC);
    END IF;
END
$$;
