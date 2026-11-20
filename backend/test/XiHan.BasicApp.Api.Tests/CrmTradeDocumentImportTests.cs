// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Trade-document OCR import: field validation, explicit line mapping and shipment creation.</summary>
public sealed class CrmTradeDocumentImportTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-trade-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<ICrmShipmentAppService> _shipments = new();
    private readonly CrmTradeDocumentImportAppService _service;
    private long _orderLineId;

    public CrmTradeDocumentImportTests()
    {
        _tenantScope = _tenant.Change(13);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        var tenant = _tenant;
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });
        _db.CodeFirst.InitTables(typeof(SysCrmTradeDocumentImportRow), typeof(SysCrmSalesOrderLine));
        var line = new SysCrmSalesOrderLine
        {
            SalesOrderId = 99,
            ProductCode = "SKU-1",
            Description = "Industrial pump",
            Quantity = 10m,
            UnitPrice = 100m
        };
        _db.Insertable(line).ExecuteCommand();
        _orderLineId = _db.Queryable<SysCrmSalesOrderLine>().First().BasicId;
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _shipments.Setup(service => service.CreateAsync(
                It.IsAny<CrmShipmentCreateDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmShipmentDto { BasicId = 777, ShipmentNumber = "INV-1" });
        _service = new CrmTradeDocumentImportAppService(
            new CrmTradeDocumentImportRepository(_resolver.Object),
            new CrmSalesOrderLineRepository(_resolver.Object),
            _shipments.Object);
    }

    [Fact]
    public void Policy_ShouldValidateMapAndGateReview()
    {
        var rows = CrmTradeDocumentPolicy.ParseRows("""
            [{"externalId":"a","documentNumber":" inv-1 ","orderReference":"PO-1","currency":"usd","totalAmount":100,
              "shipmentDate":"2026-11-20","lines":[{"sku":"SKU-1","quantity":2}]}]
            """);
        var row = Assert.Single(rows);
        Assert.Empty(row.Errors);
        Assert.Equal("0:INV-1", row.NormalizedDocumentKey);
        Assert.Equal("USD", row.Currency);
        Assert.Equal(2m, row.Lines[0].Quantity);

        var invalid = CrmTradeDocumentPolicy.ParseRows("""
            [{"externalId":"b","currency":"dollar","totalAmount":-1,"lines":[{"sku":"x","quantity":0}]}]
            """);
        Assert.Contains("document_number_required", invalid[0].Errors);
        Assert.Contains("invalid_currency", invalid[0].Errors);
        Assert.Contains("invalid_total_amount", invalid[0].Errors);
        Assert.Contains("invalid_line", invalid[0].Errors);
        Assert.Throws<ArgumentException>(() => CrmTradeDocumentPolicy.ParseRows("[]"));

        var orderLines = new[] { MakeLine(5, "sku-1", 10m) };
        Assert.Equal(5, CrmTradeDocumentPolicy.SuggestLines(row.Lines, orderLines)["SKU-1"]);
        Assert.Throws<InvalidOperationException>(() => CrmTradeDocumentPolicy.ValidateMapping(
            row.Lines, orderLines, new[] { (5L, 12m) }));
        Assert.False(CrmTradeDocumentPolicy.CanConfirm(CrmTradeDocumentImportStatus.Pending, "duplicate").Allowed);
        Assert.False(CrmTradeDocumentPolicy.CanReject(CrmTradeDocumentImportStatus.Pending, null).Allowed);
        Assert.True(CrmTradeDocumentPolicy.CanReject(CrmTradeDocumentImportStatus.Pending, "illegible").Allowed);
    }

    [Fact]
    public async Task ImportFlow_ShouldMapAndCreateShipmentOnlyOnConfirm()
    {
        var payload = """
            [{"externalId":"a","documentNumber":"INV-1","orderReference":"PO-1","carrier":"DHL",
              "trackingNumber":"TRK-1","currency":"USD","totalAmount":200,
              "lines":[{"sku":"SKU-1","quantity":2}]}]
            """;
        var input = new CrmTradeDocumentImportInputDto { RowsJson = payload, SalesOrderId = 99 };

        var preview = await _service.PreviewAsync(input);
        Assert.Equal(1, preview.Total);
        Assert.Equal(1, preview.Valid);
        Assert.Equal(_orderLineId, preview.Rows[0].Lines[0].SuggestedOrderLineId);
        Assert.Empty(await _service.GetBatchesAsync());

        var imported = await _service.ImportAsync(input);
        Assert.Equal(1, imported.Staged);
        var row = Assert.Single(await _service.GetRowsAsync(imported.BatchId));
        Assert.Equal((int)CrmTradeDocumentImportStatus.Pending, row.Status);

        var orderLine = Assert.Single(await _service.GetOrderLinesAsync(99));
        Assert.Equal(10m, orderLine.AvailableQuantity);

        var confirmed = await _service.ConfirmAsync(new CrmTradeDocumentConfirmDto
        {
            RowId = row.BasicId,
            SalesOrderId = 99,
            Lines = new[] { new CrmTradeDocumentMappedLineDto { OrderLineId = orderLine.BasicId, Quantity = 2m } }
        });
        Assert.Equal((int)CrmTradeDocumentImportStatus.Confirmed, confirmed.Status);
        Assert.Equal(777, confirmed.CreatedShipmentId);
        _shipments.Verify(
            service => service.CreateAsync(It.Is<CrmShipmentCreateDto>(dto =>
                dto.ShipmentNumber == "INV-1" && dto.SalesOrderId == 99 && dto.Lines.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfirmAsync(new CrmTradeDocumentConfirmDto
        {
            RowId = row.BasicId,
            SalesOrderId = 99,
            Lines = new[] { new CrmTradeDocumentMappedLineDto { OrderLineId = orderLine.BasicId, Quantity = 1m } }
        }));
    }

    [Fact]
    public async Task DuplicatesAndReject_ShouldStayAuditable()
    {
        var payload = """
            [
              {"externalId":"a","documentNumber":"INV-2","orderReference":"PO-2","lines":[{"sku":"SKU-1","quantity":1}]},
              {"externalId":"b","documentNumber":"inv-2","orderReference":"PO-2","lines":[{"sku":"SKU-1","quantity":1}]}
            ]
            """;
        var input = new CrmTradeDocumentImportInputDto { RowsJson = payload };
        var preview = await _service.PreviewAsync(input);
        Assert.Equal(1, preview.Duplicates);

        var imported = await _service.ImportAsync(input);
        Assert.Equal(1, imported.DuplicateStaged);
        var duplicate = (await _service.GetRowsAsync(imported.BatchId)).First(item => item.ExternalId == "b");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfirmAsync(new CrmTradeDocumentConfirmDto
        {
            RowId = duplicate.BasicId,
            SalesOrderId = 99
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RejectAsync(
            new CrmTradeDocumentDecisionDto { RowId = duplicate.BasicId }));
        var rejected = await _service.RejectAsync(new CrmTradeDocumentDecisionDto
        {
            RowId = duplicate.BasicId,
            Reason = "unreadable"
        });
        Assert.Equal((int)CrmTradeDocumentImportStatus.Rejected, rejected.Status);
        _shipments.Verify(
            service => service.CreateAsync(It.IsAny<CrmShipmentCreateDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndNoOcrEngine()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmTradeDocumentImportAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.54", "5.9.54.sql");
        Assert.Contains("Tag = \"TradeDocumentImport\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("ICrmShipmentAppService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_trade_document_import_row", migration, StringComparison.Ordinal);
    }

    private static SysCrmSalesOrderLine MakeLine(long id, string sku, decimal quantity)
    {
        var line = new SysCrmSalesOrderLine { ProductCode = sku, Quantity = quantity };
        typeof(SysCrmSalesOrderLine).GetProperty(nameof(SysCrmSalesOrderLine.BasicId))!.SetValue(line, id);
        return line;
    }
    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}