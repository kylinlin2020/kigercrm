-- 5.9.22: align prospecting Evidence and Score index names with CodeFirst metadata.
-- Older upgrade scripts used abbreviated names. Drop those names before creating the canonical
-- CodeFirst names so upgraded databases do not retain duplicate indexes after schema sync.
DO $$
BEGIN
    IF to_regclass('crm_prospecting_evidence') IS NOT NULL THEN
        DROP INDEX IF EXISTS ix_crm_prosp_evidence_tenant_candidate_field;
        DROP INDEX IF EXISTS ix_crm_prosp_evidence_tenant_provider_retrieved;
        DROP INDEX IF EXISTS ux_crm_prosp_evidence_identity;

        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_evidence_te_ca_fi
            ON crm_prospecting_evidence (tenant_id, candidate_id, field_name);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_evidence_te_pr_ri
            ON crm_prospecting_evidence (tenant_id, provider_code, retrieved_at_utc DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_evidence_evidenceidentity
            ON crm_prospecting_evidence (
                tenant_id, candidate_id, provider_code, provider_record_id,
                field_name, source_url_hash, raw_payload_hash
            );
    END IF;

    IF to_regclass('crm_prospecting_score') IS NOT NULL THEN
        DROP INDEX IF EXISTS ix_crm_prosp_score_tenant_candidate_evaluated;
        DROP INDEX IF EXISTS ix_crm_prosp_score_tenant_version_priority;

        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_score_te_ca_ev
            ON crm_prospecting_score (tenant_id, candidate_id, evaluated_at_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_crm_prospecting_score_te_sv_pr
            ON crm_prospecting_score (tenant_id, scorecard_version, priority DESC);
    END IF;
END
$$;
