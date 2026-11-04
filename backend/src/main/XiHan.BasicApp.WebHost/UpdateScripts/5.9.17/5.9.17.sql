-- 5.9.17: serialize prospecting strategy version allocation and activation per campaign.
-- Backfill the monotonic sequence from all existing versions (including soft-deleted rows).
DO $$
BEGIN
    IF to_regclass('crm_prospecting_campaign') IS NOT NULL
       AND to_regclass('crm_prospecting_strategy') IS NOT NULL THEN
        ALTER TABLE crm_prospecting_campaign
            ADD COLUMN IF NOT EXISTS strategy_version_sequence integer NOT NULL DEFAULT 0;

        UPDATE crm_prospecting_campaign AS campaign
        SET strategy_version_sequence = GREATEST(
            campaign.strategy_version_sequence,
            COALESCE((
                SELECT MAX(strategy.version)
                FROM crm_prospecting_strategy AS strategy
                WHERE strategy.tenant_id = campaign.tenant_id
                  AND strategy.campaign_id = campaign.basic_id
            ), 0)
        );

        -- The campaign pointer is authoritative. Clear orphaned activation timestamps
        -- before enforcing at most one live activated version per tenant/campaign.
        UPDATE crm_prospecting_strategy AS strategy
        SET activated_at = NULL
        WHERE strategy.activated_at IS NOT NULL
          AND strategy.is_deleted = false
          AND NOT EXISTS (
              SELECT 1
              FROM crm_prospecting_campaign AS campaign
              WHERE campaign.tenant_id = strategy.tenant_id
                AND campaign.basic_id = strategy.campaign_id
                AND campaign.active_strategy_version = strategy.version
                AND campaign.is_deleted = false
          );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prosp_strategy_tenant_campaign_active
            ON crm_prospecting_strategy (tenant_id, campaign_id)
            WHERE is_deleted = false AND activated_at IS NOT NULL;
    END IF;
END
$$;
