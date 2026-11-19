using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>Immutable audit of one verified WeCom command callback; no token or secret is stored.</summary>
[SugarTable(TableName = "Sys_Wecom_Command_Audit", TableDescription = "WeCom command gateway audit")]
[SugarIndex("IX_{table}_Te_From_Time", nameof(TenantId), OrderByType.Asc, nameof(FromUserId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("UX_{table}_Te_Msg_IsDe", nameof(TenantId), OrderByType.Asc, nameof(ProviderMessageId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysWecomCommandAudit : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Corp_Id", Length = 120, IsNullable = false)]
    public string CorpId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Agent_Id", Length = 120, IsNullable = false)]
    public string AgentId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "From_User_Id", Length = 200, IsNullable = false)]
    public string FromUserId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Provider_Message_Id", Length = 200, IsNullable = false)]
    public string ProviderMessageId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Command", Length = 80, IsNullable = false)]
    public string Command { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Outcome", IsNullable = false, DefaultValue = "0")]
    public WecomCommandOutcome Outcome { get; set; }

    [SugarColumn(ColumnName = "Reason", Length = 300, IsNullable = true)]
    public string? Reason { get; set; }

    [SugarColumn(ColumnName = "Reply_Hash", Length = 64, IsNullable = false, DefaultValue = "")]
    public string ReplyHash { get; set; } = string.Empty;
}
