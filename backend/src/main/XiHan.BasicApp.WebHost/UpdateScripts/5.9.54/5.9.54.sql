-- 5.9.54: operator-mapped trade-document OCR rows. Confirmation delegates to the existing
-- shipment creation contract; the import row records the resulting shipment for audit.
CREATE TABLE IF NOT EXISTS crm_trade_document_import_row
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
    document_type integer NOT NULL DEFAULT 0,
    document_number varchar(120) NOT NULL,
    order_reference varchar(120) NOT NULL,
    buyer varchar(200) NULL,
    seller varchar(200) NULL,
    carrier varchar(120) NULL,
    tracking_number varchar(120) NULL,
    shipment_date timestamp with time zone NULL,
    currency varchar(3) NULL,
    total_amount numeric(18,2) NOT NULL DEFAULT 0,
    lines_json text NOT NULL DEFAULT '[]',
    raw_ocr_text text NULL,
    normalized_document_key varchar(160) NOT NULL,
    status integer NOT NULL DEFAULT 0,
    sales_order_id bigint NULL,
    mapped_lines_json text NULL,
    duplicate_reason varchar(500) NULL,
    reject_reason varchar(1000) NULL,
    created_shipment_id bigint NULL,
    CONSTRAINT pk_crm_trade_document_import_row PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_crm_trade_document_import_row_te_ba_st
    ON crm_trade_document_import_row (tenant_id, batch_id, status);
CREATE INDEX IF NOT EXISTS ix_crm_trade_document_import_row_te_key
    ON crm_trade_document_import_row (tenant_id, normalized_document_key);
