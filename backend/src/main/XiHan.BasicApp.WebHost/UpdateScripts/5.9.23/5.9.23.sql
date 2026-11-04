-- 5.9.23: persist bounded, deterministic evidence references on each immutable score snapshot.
-- Existing scores receive an empty reference set; historical evidence is not guessed or backfilled.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_score') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_score
            ADD COLUMN IF NOT EXISTS evidence_references_json text NOT NULL DEFAULT '[]';
    END IF;
END
$$;
