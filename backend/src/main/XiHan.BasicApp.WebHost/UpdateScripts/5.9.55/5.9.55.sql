-- 5.9.55: question bank categories and paper selection rules. Question ids are frozen at paper
-- creation; changing the bank later never mutates an existing paper's question set.
ALTER TABLE crm_exam_question ADD COLUMN IF NOT EXISTS category varchar(80) NULL;
CREATE INDEX IF NOT EXISTS ix_crm_exam_question_te_category
    ON crm_exam_question (tenant_id, category);
ALTER TABLE crm_exam_paper ADD COLUMN IF NOT EXISTS selection_rules_json text NULL;