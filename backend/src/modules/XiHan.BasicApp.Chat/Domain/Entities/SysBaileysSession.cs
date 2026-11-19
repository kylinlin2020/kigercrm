using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>
/// Tenant-owned Baileys session contract. Only a reference to the encrypted auth-state blob is
/// stored; no QR payload, session secret or device identity is exposed through this entity.
/// A real Baileys protocol client is not wired in this slice (see development plan scope note).
/// </summary>
[SugarTable(TableName = "Sys_Baileys_Session", TableDescription = "Tenant-owned Baileys sessions")]
[SugarIndex("IX_{table}_Te_St_Health", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(Health), OrderByType.Asc)]
public sealed class SysBaileysSession : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Display_Name", Length = 160, IsNullable = false)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Reference into the tenant secret store; never a QR payload or auth-state secret.</summary>
    [SugarColumn(ColumnName = "Auth_State_Ref", Length = 300, IsNullable = true)]
    public string? AuthStateRef { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public BaileysSessionStatus Status { get; set; }

    [SugarColumn(ColumnName = "Health", IsNullable = false, DefaultValue = "0")]
    public BaileysHealthState Health { get; set; }

    [SugarColumn(ColumnName = "Qr_Issued_At_Utc", IsNullable = true)]
    public DateTimeOffset? QrIssuedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Last_Connected_At_Utc", IsNullable = true)]
    public DateTimeOffset? LastConnectedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Note", Length = 500, IsNullable = true)]
    public string? Note { get; set; }
}
