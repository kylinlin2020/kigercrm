// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using SqlSugar;
using XiHan.BasicApp.Core.Entities;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A person associated with a CRM customer account.
/// </summary>
[SugarTable(TableName = "Crm_Contact", TableDescription = "KIGER CRM customer contacts")]
[SugarIndex("IX_{table}_TeId_CuId", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc)]
[SugarIndex("UX_{table}_TeId_PrCuId", nameof(TenantId), OrderByType.Asc, nameof(PrimaryCustomerId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_OwId", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
public sealed class SysCrmContact : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Customer_Id", ColumnDescription = "Customer account id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "First_Name", ColumnDescription = "First name", Length = 100, IsNullable = false)]
    public string FirstName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Last_Name", ColumnDescription = "Last name", Length = 100, IsNullable = false)]
    public string LastName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Job_Title", ColumnDescription = "Job title", Length = 160, IsNullable = true)]
    public string? JobTitle { get; set; }

    [SugarColumn(ColumnName = "Email", ColumnDescription = "Email address", Length = 254, IsNullable = true)]
    public string? Email { get; set; }

    [SugarColumn(ColumnName = "Phone", ColumnDescription = "Phone number", Length = 40, IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(ColumnName = "WhatsApp", ColumnDescription = "WhatsApp number", Length = 40, IsNullable = true)]
    public string? WhatsApp { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", ColumnDescription = "Owner user id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Is_Primary", ColumnDescription = "Primary contact")]
    public bool IsPrimary { get; set; }

    /// <summary>Customer id only while this contact is primary; null permits multiple non-primary contacts.</summary>
    [SugarColumn(ColumnName = "Primary_Customer_Id", ColumnDescription = "Unique primary-contact slot", IsNullable = true)]
    public long? PrimaryCustomerId { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDescription = "Internal notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }
}
