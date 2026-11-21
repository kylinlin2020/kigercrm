-- 5.9.57: knowledge document tags and content hash for classification and duplicate detection.
ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS tags_json text NULL;
ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS content_hash varchar(64) NULL;
CREATE INDEX IF NOT EXISTS ix_sys_knowledge_document_te_hash
    ON sys_knowledge_document (tenant_id, content_hash);