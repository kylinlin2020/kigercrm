-- 5.9.51: knowledge document review audit. Decoupled from indexing status: a document may be
-- indexed but still await review, and a later content change requires a fresh decision.
CREATE TABLE IF NOT EXISTS sys_knowledge_document_review
(
    basic_id bigint NOT NULL,
    tenant_id bigint NOT NULL,
    document_id bigint NOT NULL,
    status integer NOT NULL DEFAULT 0,
    version_hash varchar(64) NOT NULL,
    comment varchar(1000) NULL,
    reviewer_id bigint NULL,
    reviewed_at_utc timestamp with time zone NULL,
    created_id bigint NULL,
    created_time timestamp with time zone NOT NULL,
    CONSTRAINT pk_sys_knowledge_document_review PRIMARY KEY (basic_id)
);
CREATE INDEX IF NOT EXISTS ix_sys_knowledge_document_review_te_do_time
    ON sys_knowledge_document_review (tenant_id, document_id, created_time DESC);
