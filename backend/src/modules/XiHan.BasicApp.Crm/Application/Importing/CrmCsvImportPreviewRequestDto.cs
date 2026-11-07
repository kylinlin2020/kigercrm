// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace XiHan.BasicApp.Crm.Application.Importing;

/// <summary>Request for a side-effect-free CRM CSV import preview. FileBytes contains UTF-8 CSV data.</summary>
public sealed class CrmCsvImportPreviewRequestDto
{
    public byte[] FileBytes { get; set; } = [];

    /// <summary>Optional source-header to server-allowlisted CRM target-field mapping.</summary>
    public Dictionary<string, string>? FieldMapping { get; set; }
}
