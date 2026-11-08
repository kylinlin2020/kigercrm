namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppWebhookInboxMigrationTests
{
    [Fact]
    public void InitialMigration_ShouldCreateTenantScopedInbox()
    {
        var sql = ReadMigration("5.9.39", "5.9.39.sql");

        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_whatsapp_webhook_inbox", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_id bigint NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("whatsapp_business_account_id varchar(100) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("provider_message_id varchar(200) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("payload_json text NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ON sys_whatsapp_webhook_inbox (tenant_id, whatsapp_business_account_id, provider_message_id)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialMigration_ShouldConstrainRetryStateAndInstallAuditIndexes()
    {
        var sql = ReadMigration("5.9.39", "5.9.39.sql");

        Assert.Contains("attempt_count >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("'Pending', 'Retryable', 'Processed', 'NeedsReview'", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_teid_crti", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_crid", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_teid_st_neat", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EventIdempotencyMigration_ShouldBackfillLegacyRowsAndReplaceMessageOnlyIndex()
    {
        var sql = ReadMigration("5.9.40", "5.9.40.sql");

        Assert.Contains("ADD COLUMN IF NOT EXISTS event_key varchar(64)", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS event_type varchar(32)", sql, StringComparison.Ordinal);
        Assert.Contains("SET event_key = md5(", sql, StringComparison.Ordinal);
        Assert.Contains("event_type = 'InboundMessage'", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN event_key SET NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS ux_sys_whatsapp_webhook_inbox_teid_waid_meid", sql, StringComparison.Ordinal);
        Assert.Contains("ON sys_whatsapp_webhook_inbox (tenant_id, whatsapp_business_account_id, event_key)", sql, StringComparison.Ordinal);
    }

    private static string ReadMigration(string version, string fileName) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", version, fileName)));

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
