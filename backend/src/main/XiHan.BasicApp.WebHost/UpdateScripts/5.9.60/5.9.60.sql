-- 5.9.60: knowledge document visibility and role allowlist for application-layer ACL.
-- Restricted documents are hidden from lists/details and filtered out of RAG citations.
ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS visibility integer NOT NULL DEFAULT 0;
ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS allowed_roles varchar(500) NULL;
CREATE INDEX IF NOT EXISTS ix_sys_knowledge_document_te_vi
    ON sys_knowledge_document (tenant_id, visibility);