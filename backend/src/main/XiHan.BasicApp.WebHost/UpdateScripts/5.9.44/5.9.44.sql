-- 5.9.44: approved recurring prospecting schedules. The scheduler advances Next_Run_At_Utc with a
-- compare-and-swap and delegates Run creation to the existing approved StartRun pipeline.
CREATE TABLE IF NOT EXISTS crm_prospecting_schedule
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
    request_key_prefix varchar(100) NOT NULL,
    time_zone_id varchar(100) NOT NULL DEFAULT 'UTC',
    frequency integer NOT NULL DEFAULT 0,
    interval integer NOT NULL DEFAULT 1,
    time_of_day_minutes integer NOT NULL DEFAULT 0,
    day_of_week integer NULL,
    day_of_month integer NULL,
    start_date_utc timestamp with time zone NOT NULL,
    end_date_utc timestamp with time zone NULL,
    status integer NOT NULL DEFAULT 0,
    next_run_at_utc timestamp with time zone NULL,
    last_run_at_utc timestamp with time zone NULL,
    last_run_id bigint NULL,
    approved_by bigint NULL,
    approved_at timestamp with time zone NULL,
    CONSTRAINT pk_crm_prospecting_schedule PRIMARY KEY (basic_id)
);

CREATE INDEX IF NOT EXISTS ix_crm_prospecting_schedule_te_ca_crti
    ON crm_prospecting_schedule (tenant_id, campaign_id, created_time DESC);

CREATE INDEX IF NOT EXISTS ix_crm_prospecting_schedule_te_st_next
    ON crm_prospecting_schedule (tenant_id, status, next_run_at_utc);

CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_schedule_te_rq_isde
    ON crm_prospecting_schedule (tenant_id, request_key_prefix, is_deleted);
