using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Regression guards for the permanent CRM create-request ledger integration.</summary>
public sealed class CrmCreateRequestLedgerWiringTests
{
    [Fact]
    public void LedgerEntity_ShouldBeCreationOnlyAndUniquelyScopedByTenantTypeAndKey()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmCreateRequest.cs");
        Assert.Contains("BasicAppCreationEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(DocumentType)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(RequestKey)", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerRepository_ShouldUseStrictTenantFilteredQuery()
    {
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmCreateRequestRepository.cs");
        Assert.Contains("CreateQueryable()", repository, StringComparison.Ordinal);
        Assert.Contains("request.DocumentType == documentType", repository, StringComparison.Ordinal);
        Assert.Contains("request.RequestKey == requestKey", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateNoTenantQueryable", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderCreate_ShouldReadAndWriteLedgerInsideTheAggregateUnitOfWork()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal);
        var end = source.IndexOf("\n    [UnitOfWork(true)]", start + 30, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.True(method.IndexOf("createRequests.GetByRequestKeyAsync", StringComparison.Ordinal) <
                    method.IndexOf("repository.AddAsync(order", StringComparison.Ordinal));
        Assert.True(method.IndexOf("orderLines.AddAsync(line", StringComparison.Ordinal) <
                    method.LastIndexOf("createRequests.AddAsync", StringComparison.Ordinal));
        Assert.Contains("DocumentType = \"SALES_ORDER\"", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreate_ShouldReplayBeforeReservationAndWriteLedgerAfterShipmentLines()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var start = source.IndexOf("CreateAsync(CrmShipmentCreateDto", StringComparison.Ordinal);
        var end = source.IndexOf("\n    [UnitOfWork(true)]", start + 30, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.True(method.IndexOf("createRequests.GetByRequestKeyAsync", StringComparison.Ordinal) <
                    method.IndexOf("order.FulfillmentRevision++", StringComparison.Ordinal));
        Assert.True(method.IndexOf("shipmentLines.AddAsync(line", StringComparison.Ordinal) <
                    method.LastIndexOf("createRequests.AddAsync", StringComparison.Ordinal));
        Assert.Contains("DocumentType = \"SHIPMENT\"", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRequestLedgerMigration_ShouldCreatePermanentUniqueKeyWithoutSoftDelete()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.10", "5.9.10.sql");
        Assert.Contains("to_regclass('crm_sales_order')", migration, StringComparison.Ordinal);
        Assert.Contains("to_regclass('crm_shipment')", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_create_request", migration, StringComparison.Ordinal);
        Assert.Contains("UNIQUE INDEX IF NOT EXISTS", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, document_type, request_key)", migration, StringComparison.Ordinal);
        var executableSql = string.Join("\n", migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Assert.DoesNotContain("is_deleted", executableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LedgerMigration_ShouldMatchCreationAuditEntityBaseColumns()
    {
        // SugarMultiTenantCreationEntity maps these inherited properties to the following
        // PostgreSQL identifiers; keep the hand-written migration aligned with CodeFirst.
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.10", "5.9.10.sql");

        Assert.Contains("basic_id bigint NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("row_version bigint NOT NULL DEFAULT 0", migration, StringComparison.Ordinal);
        Assert.Contains("tenant_id bigint NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("created_time timestamp with time zone NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("created_id bigint NULL", migration, StringComparison.Ordinal);
        Assert.Contains("created_by text NULL", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_ShouldUsePersistedOwnerWhenOwnerWasOmitted()
    {
        var salesOrder = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var shipment = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");

        Assert.Contains("input.OwnerUserId ?? replayOrder.OwnerUserId", salesOrder, StringComparison.Ordinal);
        Assert.Contains("input.OwnerUserId ?? replayShipment.OwnerUserId", shipment, StringComparison.Ordinal);
        Assert.Contains("existingRequest.PayloadHash, replayFingerprint", salesOrder, StringComparison.Ordinal);
        Assert.Contains("existingRequest.PayloadHash, replayFingerprint", shipment, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
