namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppWebhookInboxMigrationTests
{
    [Fact]
    public void Migration_ShouldCreateTenantScopedInboxWithProviderMessageIdempotency()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.39", "5.9.39.sql")));

        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_whatsapp_webhook_inbox", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_id bigint NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("whatsapp_business_account_id varchar(100) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("provider_message_id varchar(200) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("payload_json text NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ON sys_whatsapp_webhook_inbox (tenant_id, whatsapp_business_account_id, provider_message_id)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ShouldConstrainRetryStateAndInstallAuditIndexes()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.39", "5.9.39.sql")));

        Assert.Contains("attempt_count >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("'Pending', 'Retryable', 'Processed', 'NeedsReview'", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_teid_crti", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_crid", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_teid_st_neat", sql, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
