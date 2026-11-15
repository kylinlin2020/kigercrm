-- 5.9.42: shortlist marker on prospecting review candidates, orthogonal to the review state machine.
-- NULL means not shortlisted; the existing ix_crm_prospecting_candidate_te_ru_st_cr index already
-- bounds every shortlist query by (tenant, run), so no dedicated index is added here.
ALTER TABLE crm_prospecting_candidate
    ADD COLUMN IF NOT EXISTS shortlisted_at timestamp with time zone;
