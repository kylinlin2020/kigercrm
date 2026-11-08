namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppWebhookInboxLeaseMigrationTests
{
    [Fact]
    public void LeaseMigration_ShouldAddFencingColumnsAndDueWorkIndex()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.42", "5.9.42.sql");
        var sql = File.ReadAllText(path);

        Assert.Contains("ADD COLUMN IF NOT EXISTS lease_owner varchar(160)", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS lease_token uuid", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS lease_expires_at timestamp with time zone", sql, StringComparison.Ordinal);
        Assert.Contains("fencing_version bigint NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("ix_sys_whatsapp_webhook_inbox_teid_st_neat_lease", sql, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, status, next_attempt_time, lease_expires_at)", sql, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
