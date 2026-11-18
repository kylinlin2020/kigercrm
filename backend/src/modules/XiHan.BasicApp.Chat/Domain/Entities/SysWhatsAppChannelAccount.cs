using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>
/// Server-owned WABA/phone-number binding to one tenant. Only a reference to the encrypted
/// credential is stored here; no token or app secret ever appears in this table or API responses.
/// Without an active binding the Meta webhook stays fail-closed (503 for provider retry).
/// </summary>
[SugarTable(TableName = "Sys_WhatsApp_Channel_Account", TableDescription = "Tenant-owned WhatsApp channel accounts")]
[SugarIndex("UX_{table}_Te_Wa_Ph_IsDe", nameof(TenantId), OrderByType.Asc, nameof(WhatsAppBusinessAccountId), OrderByType.Asc, nameof(PhoneNumberId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_St", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class SysWhatsAppChannelAccount : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Display_Name", Length = 160, IsNullable = false)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "WhatsApp_Business_Account_Id", Length = 100, IsNullable = false)]
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Phone_Number_Id", Length = 100, IsNullable = false)]
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Reference to a tenant-encrypted credential in the secrets store, never plaintext.</summary>
    [SugarColumn(ColumnName = "Credential_Ref", Length = 300, IsNullable = true)]
    public string? CredentialRef { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public WhatsAppChannelAccountStatus Status { get; set; }
}
