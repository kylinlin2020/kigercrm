-- 5.9.59: seeded deterministic random selection for exam papers. Question ids remain frozen at
-- creation; the seed makes a randomised draw reproducible.
ALTER TABLE crm_exam_paper ADD COLUMN IF NOT EXISTS randomize_selection boolean NOT NULL DEFAULT false;
ALTER TABLE crm_exam_paper ADD COLUMN IF NOT EXISTS selection_seed integer NULL;