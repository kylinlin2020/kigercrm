// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using SqlSugar;
using XiHan.BasicApp.Core.Entities;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// CRM customer account. Tenant ownership and audit fields are inherited from BasicApp.
/// </summary>
[SugarTable(TableName = "Crm_Customer", TableDescription = "KIGER CRM customer accounts")]
[SugarIndex("UX_{table}_TeId_Na_IsDe", nameof(TenantId), OrderByType.Asc, nameof(Name), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_TeId_OwId", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
public sealed class SysCrmCustomer : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Name", ColumnDescription = "Customer name", Length = 200, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Legal_Name", ColumnDescription = "Registered legal name", Length = 250, IsNullable = true)]
    public string? LegalName { get; set; }

    [SugarColumn(ColumnName = "Website", ColumnDescription = "Company website", Length = 500, IsNullable = true)]
    public string? Website { get; set; }

    [SugarColumn(ColumnName = "Country_Region", ColumnDescription = "Country or region", Length = 100, IsNullable = true)]
    public string? CountryRegion { get; set; }

    [SugarColumn(ColumnName = "Industry", ColumnDescription = "Industry", Length = 120, IsNullable = true)]
    public string? Industry { get; set; }

    [SugarColumn(ColumnName = "Source", ColumnDescription = "Acquisition source", Length = 80, IsNullable = true)]
    public string? Source { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", ColumnDescription = "Owner user id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDescription = "Internal notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }

    [SugarColumn(ColumnName = "External_System", ColumnDescription = "Source system", Length = 80, IsNullable = true)]
    public string? ExternalSystem { get; set; }

    [SugarColumn(ColumnName = "External_Id", ColumnDescription = "Source system record id", Length = 160, IsNullable = true)]
    public string? ExternalId { get; set; }
}
