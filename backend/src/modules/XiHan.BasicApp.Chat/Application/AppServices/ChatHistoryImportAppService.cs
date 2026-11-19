using XiHan.BasicApp.Chat.Application;
using XiHan.BasicApp.Chat.Application.Contracts;
using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Permissions;
using XiHan.BasicApp.Chat.Domain.Prospecting;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Chat.Application.AppServices;

/// <summary>
/// Staged history import: preview is pure, import produces an auditable batch of row verdicts and
/// never fabricates chat conversations. Replaying the same external ids reports duplicates instead
/// of silently inserting them again.
/// </summary>
[DynamicApi(Group = "BasicApp.Chat", GroupName = "在线聊天", Tag = "HistoryImport")]
public sealed class ChatHistoryImportAppService(
    IChatHistoryImportBatchRepository batches,
    IChatHistoryImportRowRepository rows)
    : ChatApplicationService, IChatHistoryImportAppService
{
    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<ChatHistoryImportResultDto> PreviewAsync(
        ChatHistoryImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var verdicts = await EvaluateAsync(input, cancellationToken);
        return ToResult(0, verdicts, input.Rows);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(ChatPermissionCodes.Manage)]
    public async Task<ChatHistoryImportResultDto> ImportAsync(
        ChatHistoryImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var verdicts = await EvaluateAsync(input, cancellationToken);
        var batch = await batches.AddAsync(new SysChatHistoryImportBatch
        {
            FileName = input.FileName.Trim(),
            SourceChannel = input.SourceChannel.Trim(),
            TotalCount = input.Rows.Count,
            Status = ChatHistoryImportBatchStatus.Imported
        }, cancellationToken);

        var imported = 0;
        var duplicates = 0;
        var invalid = 0;
        for (var index = 0; index < verdicts.Count; index++)
        {
            var row = input.Rows[index];
            var verdict = verdicts[index];
            if (verdict.Status == ChatHistoryImportRowStatus.Imported) imported++;
            if (verdict.Status == ChatHistoryImportRowStatus.Duplicate) duplicates++;
            if (verdict.Status == ChatHistoryImportRowStatus.Invalid) invalid++;
            await rows.AddAsync(new SysChatHistoryImportRow
            {
                BatchId = batch.BasicId,
                SourceChannel = input.SourceChannel.Trim(),
                ExternalId = row.ExternalId?.Trim() ?? string.Empty,
                Sender = row.Sender?.Trim() ?? string.Empty,
                Recipient = row.Recipient?.Trim() ?? string.Empty,
                ContentHash = verdict.ContentHash,
                SentAtUtc = verdict.SentAtUtc,
                Status = verdict.Status,
                Reason = verdict.Reason
            }, cancellationToken);
        }
        batch.ImportedCount = imported;
        batch.DuplicateCount = duplicates;
        batch.InvalidCount = invalid;
        await batches.UpdateAsync(batch, cancellationToken);
        return ToResult(batch.BasicId, verdicts, input.Rows);
    }

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<ChatHistoryImportBatchDto>> GetBatchesAsync(CancellationToken cancellationToken = default)
        => (await batches.GetRecentAsync(50, cancellationToken)).Select(item => new ChatHistoryImportBatchDto
        {
            BasicId = item.BasicId,
            FileName = item.FileName,
            SourceChannel = item.SourceChannel,
            TotalCount = item.TotalCount,
            ImportedCount = item.ImportedCount,
            DuplicateCount = item.DuplicateCount,
            InvalidCount = item.InvalidCount,
            Status = (int)item.Status,
            CreatedTime = item.CreatedTime
        }).ToArray();

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<ChatHistoryImportVerdictDto>> GetRowsAsync(long batchId, CancellationToken cancellationToken = default)
        => (await rows.GetByBatchAsync(batchId, cancellationToken)).Select(item => new ChatHistoryImportVerdictDto
        {
            ExternalId = item.ExternalId,
            Status = (int)item.Status,
            Reason = item.Reason,
            SentAtUtc = item.SentAtUtc,
            ContentHash = item.ContentHash
        }).ToArray();

    private async Task<IReadOnlyList<ChatHistoryImportVerdict>> EvaluateAsync(
        ChatHistoryImportInputDto input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceChannel);
        if (input.SourceChannel.Length > 40) throw new ArgumentOutOfRangeException(nameof(input.SourceChannel));
        var existing = (await rows.GetBySourceChannelAsync(input.SourceChannel.Trim(), cancellationToken))
            .Select(item => item.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        return ChatHistoryImportPolicy.Evaluate(
            input.Rows.Select(row => new ChatHistoryImportSourceRow(
                row.ExternalId, row.Sender, row.Recipient, row.Content, row.SentAt)).ToArray(),
            externalId => existing.Contains(externalId));
    }

    private static ChatHistoryImportResultDto ToResult(
        long batchId,
        IReadOnlyList<ChatHistoryImportVerdict> verdicts,
        IReadOnlyList<ChatHistoryImportRowDto> inputRows) => new()
    {
        BatchId = batchId,
        Total = verdicts.Count,
        Imported = verdicts.Count(item => item.Status == ChatHistoryImportRowStatus.Imported),
        Duplicates = verdicts.Count(item => item.Status == ChatHistoryImportRowStatus.Duplicate),
        Invalid = verdicts.Count(item => item.Status == ChatHistoryImportRowStatus.Invalid),
        Rows = verdicts.Select((verdict, index) => new ChatHistoryImportVerdictDto
        {
            ExternalId = inputRows[index].ExternalId ?? string.Empty,
            Status = (int)verdict.Status,
            Reason = verdict.Reason,
            SentAtUtc = verdict.SentAtUtc,
            ContentHash = verdict.ContentHash
        }).ToArray()
    };
}
