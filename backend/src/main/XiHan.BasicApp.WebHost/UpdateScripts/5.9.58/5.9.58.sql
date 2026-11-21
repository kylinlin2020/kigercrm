-- 5.9.58: track the hash of the last successfully indexed raw content so incremental
-- re-indexing can skip documents whose content did not change.
ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS indexed_content_hash varchar(64) NULL;