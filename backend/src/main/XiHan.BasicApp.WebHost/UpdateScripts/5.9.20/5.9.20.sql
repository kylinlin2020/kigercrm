-- 5.9.20: enforce arithmetic integrity for immutable prospecting score snapshots.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_score') IS NOT NULL THEN
        IF EXISTS (
            SELECT 1
            FROM crm_prospecting_score
            WHERE total <> identity_points + procurement_points + product_fit_points
                + market_fit_points + recency_points + evidence_points + contact_points
        ) THEN
            RAISE EXCEPTION 'Cannot add prospecting score integrity constraint: existing component totals do not match total.';
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conname = 'ck_crm_prospecting_score_component_total'
              AND conrelid = 'crm_prospecting_score'::regclass
        ) THEN
            ALTER TABLE crm_prospecting_score
                ADD CONSTRAINT ck_crm_prospecting_score_component_total
                CHECK (
                    total = identity_points + procurement_points + product_fit_points
                        + market_fit_points + recency_points + evidence_points + contact_points
                );
        END IF;
    END IF;
END
$$;
