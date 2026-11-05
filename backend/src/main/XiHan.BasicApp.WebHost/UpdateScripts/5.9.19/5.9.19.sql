-- 5.9.19: immutable, versioned prospecting score history.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_candidate') IS NOT NULL THEN
        CREATE TABLE IF NOT EXISTS crm_prospecting_score (
            basic_id bigint PRIMARY KEY,
            tenant_id bigint NOT NULL,
            candidate_id bigint NOT NULL,
            scorecard_version varchar(80) NOT NULL,
            total integer NOT NULL CHECK (total BETWEEN 0 AND 100),
            priority integer NOT NULL CHECK (priority BETWEEN 0 AND 3),
            identity_points integer NOT NULL CHECK (identity_points BETWEEN 0 AND 15),
            procurement_points integer NOT NULL CHECK (procurement_points BETWEEN 0 AND 30),
            product_fit_points integer NOT NULL CHECK (product_fit_points BETWEEN 0 AND 20),
            market_fit_points integer NOT NULL CHECK (market_fit_points BETWEEN 0 AND 10),
            recency_points integer NOT NULL CHECK (recency_points BETWEEN 0 AND 10),
            evidence_points integer NOT NULL CHECK (evidence_points BETWEEN 0 AND 10),
            contact_points integer NOT NULL CHECK (contact_points BETWEEN 0 AND 5),
            review_gates_json text NOT NULL DEFAULT '[]',
            evaluated_at_utc timestamptz NOT NULL,
            created_id bigint NULL,
            created_time timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT fk_crm_prospecting_score_candidate
                FOREIGN KEY (tenant_id, candidate_id) REFERENCES crm_prospecting_candidate (tenant_id, basic_id)
        );

        CREATE INDEX IF NOT EXISTS ix_crm_prosp_score_tenant_candidate_evaluated
            ON crm_prospecting_score (tenant_id, candidate_id, evaluated_at_utc DESC);

        CREATE INDEX IF NOT EXISTS ix_crm_prosp_score_tenant_version_priority
            ON crm_prospecting_score (tenant_id, scorecard_version, priority DESC);
    END IF;
END
$$;
