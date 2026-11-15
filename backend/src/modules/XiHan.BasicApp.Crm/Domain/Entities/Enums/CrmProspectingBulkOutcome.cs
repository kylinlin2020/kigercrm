namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Per-item verdict reported by a bulk prospecting operation; failures carry the reason.</summary>
public enum CrmProspectingBulkOutcome
{
    /// <summary>The item's state transition committed.</summary>
    Success = 0,

    /// <summary>The item already satisfied the requested state (idempotent no-op) and was reported instead of redone.</summary>
    Skipped = 1,

    /// <summary>The item was rejected by validation, the state machine or authorization; only this item rolled back.</summary>
    Failed = 2
}
