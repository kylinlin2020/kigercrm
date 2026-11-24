-- 5.9.64: globally unique webhook route keys. A (WABA, phone) or (corpId, agentId) pair must
-- belong to exactly one tenant, otherwise an anonymous callback resolves to an arbitrary tenant.
-- Partial unique indexes cover only live active bindings; soft-deleted/disabled rows are free.
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_route_global
    ON sys_whatsapp_channel_account (whatsapp_business_account_id, phone_number_id)
    WHERE is_deleted = false AND status = 0;
CREATE UNIQUE INDEX IF NOT EXISTS ux_wecom_route_global
    ON sys_wecom_account_binding (corp_id, agent_id)
    WHERE is_deleted = false AND status = 0;