// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace XiHan.BasicApp.Crm.Application.Importing;

/// <summary>Request for a side-effect-free CSV lead import preview. FileBytes is UTF-8 CSV content.</summary>
public sealed class CrmLeadImportPreviewRequestDto
{
    public byte[] FileBytes { get; set; } = [];

    /// <summary>Optional source-header to allow-listed SysCrmLead property mapping.</summary>
    public Dictionary<string, string>? FieldMapping { get; set; }
}
