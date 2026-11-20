-- 5.9.53: operator-reviewed business-card OCR rows. A row only becomes a Lead after an explicit
-- confirmation; duplicates and invalid rows are kept as records but never auto-created.
CREATE TABLE IF NOT EXISTS crm_business_card_import_row
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
    batch_id varchar(80) NOT NULL,
    external_id varchar(80) NOT NULL,
    contact_name varchar(200) NULL,
    company_name varchar(200) NULL,
    phone varchar(40) NULL,
    email varchar(254) NULL,
    job_title varchar(120) NULL,
    website varchar(500) NULL,
    raw_ocr_text text NULL,
    normalized_email varchar(254) NULL,
    phone_digits varchar(40) NULL,
    website_host varchar(500) NULL,
    status integer NOT NULL DEFAULT 0,
    duplicate_reason varchar(500) NULL,
    reject_reason varchar(1000) NULL,
    converted_lead_id bigint NULL,
    CONSTRAINT pk_crm_business_card_import_row PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_business_card_import_row_te_ba_st
    ON crm_business_card_import_row (tenant_id, batch_id, status);
