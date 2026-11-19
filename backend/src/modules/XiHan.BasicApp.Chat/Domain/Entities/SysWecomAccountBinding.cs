using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>
/// Tenant-owned WeCom (企业微信) account binding. Only a reference to the encrypted credential is
/// stored; no CorpSecret or callback token appears here or in API responses.
/// </summary>
[SugarTable(TableName = "Sys_Wecom_Account_Binding", TableDescription = "Tenant-owned WeCom bindings")]
[SugarIndex("UX_{table}_Te_Corp_Agent_IsDe", nameof(TenantId), OrderByType.Asc, nameof(CorpId), OrderByType.Asc, nameof(AgentId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysWecomAccountBinding : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Display_Name", Length = 160, IsNullable = false)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Corp_Id", Length = 120, IsNullable = false)]
    public string CorpId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Agent_Id", Length = 120, IsNullable = false)]
    public string AgentId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Credential_Ref", Length = 300, IsNullable = true)]
    public string? CredentialRef { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public WecomBindingStatus Status { get; set; }
}
