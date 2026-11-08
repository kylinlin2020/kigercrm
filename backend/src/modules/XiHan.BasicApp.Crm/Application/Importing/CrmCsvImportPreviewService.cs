// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace XiHan.BasicApp.Crm.Application.Importing;

/// <summary>
/// Bounded, side-effect-free CSV import preview. The caller supplies the resource's
/// allow-listed target fields and required fields; this service never writes CRM data.
/// </summary>
public static class CrmCsvImportPreviewService
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRows = 5_000;
    public const int MaxColumns = 100;
    public const int MaxCellCharacters = 4_000;

    public static CrmCsvImportPreviewResult Preview(
        ReadOnlyMemory<byte> fileBytes,
        IReadOnlyCollection<string> allowedTargetFields,
        IReadOnlyCollection<string> requiredTargetFields,
        IReadOnlyDictionary<string, string>? fieldMapping = null,
        int previewLimit = 100,
        IReadOnlyDictionary<string, Func<string, bool>>? fieldValidators = null)
    {
        ArgumentNullException.ThrowIfNull(allowedTargetFields);
        ArgumentNullException.ThrowIfNull(requiredTargetFields);
        if (fileBytes.Length == 0 || fileBytes.Length > MaxFileBytes)
            throw new ArgumentException($"CSV 文件必须为 1 到 {MaxFileBytes} 字节。", nameof(fileBytes));

        var allowed = new HashSet<string>(allowedTargetFields, StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0 || allowed.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("必须提供有效的目标字段白名单。", nameof(allowedTargetFields));
        var required = new HashSet<string>(requiredTargetFields, StringComparer.OrdinalIgnoreCase);
        if (!required.IsSubsetOf(allowed))
            throw new ArgumentException("必填字段必须属于目标字段白名单。", nameof(requiredTargetFields));

        if (fieldValidators is not null)
        {
            foreach (var validator in fieldValidators)
            {
                if (string.IsNullOrWhiteSpace(validator.Key) || !allowed.Contains(validator.Key))
                    throw new ArgumentException("字段验证器只能绑定白名单中的目标字段。", nameof(fieldValidators));
                if (validator.Value is null)
                    throw new ArgumentException("字段验证器不能为空。", nameof(fieldValidators));
            }
        }

        string csv;
        try
        {
            csv = new UTF8Encoding(false, true).GetString(fileBytes.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("CSV 文件必须使用有效 UTF-8 编码。", nameof(fileBytes), exception);
        }

        if (csv.Length > 0 && csv[0] == '\uFEFF')
            csv = csv[1..];

        var records = ParseRecords(csv);
        if (records.Count < 2)
            throw new ArgumentException("CSV 至少需要一行表头和一行数据。", nameof(fileBytes));
        if (records[0].Count > MaxColumns)
            throw new ArgumentException($"CSV 列数不能超过 {MaxColumns}。", nameof(fileBytes));

        var headers = records[0].Select((value, index) =>
        {
            var header = value.Trim();
            if (header.Length == 0)
                throw new ArgumentException($"第 {index + 1} 列表头为空。", nameof(fileBytes));
            if (header.Length > 128)
                throw new ArgumentException($"第 {index + 1} 列表头过长。", nameof(fileBytes));
            return header;
        }).ToArray();
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new ArgumentException("CSV 存在重复表头，请先整理文件。", nameof(fileBytes));

        // Reject stale or misspelled source-header mappings instead of silently ignoring them.
        // A preview must make it explicit when the caller's mapping does not match this file.
        Dictionary<string, string>? normalizedFieldMapping = null;
        if (fieldMapping is not null)
        {
            normalizedFieldMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fieldMapping)
            {
                var sourceHeader = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(sourceHeader) ||
                    !headers.Contains(sourceHeader, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("字段映射包含当前 CSV 中不存在的源列表头。", nameof(fieldMapping));
                if (!normalizedFieldMapping.TryAdd(sourceHeader, pair.Value))
                    throw new ArgumentException("字段映射包含重复的源列表头。", nameof(fieldMapping));
            }
        }

        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            string? mapped = null;
            var explicitlyMapped = normalizedFieldMapping is not null && normalizedFieldMapping.TryGetValue(header, out mapped);
            var target = explicitlyMapped ? mapped?.Trim() : header;
            if (string.IsNullOrWhiteSpace(target))
            {
                if (explicitlyMapped)
                    throw new ArgumentException($"CSV 列 {header} 的目标字段映射不能为空。", nameof(fieldMapping));
                continue;
            }
            if (!allowed.Contains(target))
            {
                if (explicitlyMapped)
                    throw new ArgumentException($"目标字段 {target} 不在当前资源的导入白名单中。", nameof(fieldMapping));
                continue;
            }
            if (mapping.Values.Contains(target, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"多个 CSV 列不能同时映射到字段 {target}。", nameof(fieldMapping));
            mapping[header] = target;
        }

        var mappedTargets = new HashSet<string>(mapping.Values, StringComparer.OrdinalIgnoreCase);
        var missingRequired = required.Where(field => !mappedTargets.Contains(field)).ToArray();
        var dataRows = records.Skip(1).ToArray();
        if (dataRows.Length > MaxRows)
            throw new ArgumentException($"CSV 数据行数不能超过 {MaxRows}。", nameof(fileBytes));

        var rows = new List<CrmCsvImportPreviewRow>(Math.Min(dataRows.Length, Math.Clamp(previewLimit, 1, 500)));
        var errorCount = 0;
        for (var index = 0; index < dataRows.Length; index++)
        {
            var source = dataRows[index];
            if (source.Count > MaxColumns)
                throw new ArgumentException($"CSV 列数不能超过 {MaxColumns}。", nameof(fileBytes));
            var errors = new List<string>();
            if (source.Count != headers.Length)
                errors.Add($"列数不匹配：预期 {headers.Length} 列，实际 {source.Count} 列。");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < Math.Min(source.Count, headers.Length); column++)
            {
                var value = source[column];
                if (value.Length > MaxCellCharacters)
                {
                    errors.Add($"第 {column + 1} 列内容超过 {MaxCellCharacters} 个字符。");
                    continue;
                }
                if (mapping.TryGetValue(headers[column], out var target))
                    values[target] = value.Trim();
            }

            foreach (var field in required)
            {
                if (!values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                    errors.Add($"必填字段 {field} 不能为空。");
            }

            if (fieldValidators is not null)
            {
                foreach (var validator in fieldValidators)
                {
                    if (values.TryGetValue(validator.Key, out var value) &&
                        !string.IsNullOrWhiteSpace(value) &&
                        !validator.Value(value))
                        errors.Add($"字段 {validator.Key} 的格式无效。");
                }
            }

            if (errors.Count > 0) errorCount++;
            if (rows.Count < Math.Clamp(previewLimit, 1, 500))
                rows.Add(new CrmCsvImportPreviewRow(index + 2, values, errors));
        }

        return new CrmCsvImportPreviewResult(
            headers,
            mapping,
            missingRequired,
            dataRows.Length,
            errorCount,
            dataRows.Length - errorCount,
            rows);
    }

    private static List<List<string>> ParseRecords(string csv)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var afterQuote = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        afterQuote = true;
                    }
                }
                else
                {
                    cell.Append(character);
                }
                continue;
            }

            if (afterQuote && character != ',' && character != '\r' && character != '\n' && !char.IsWhiteSpace(character))
                throw new ArgumentException("CSV 引号闭合后存在非法字符。", nameof(csv));
            if (character == '"')
            {
                if (cell.Length != 0 || afterQuote)
                    throw new ArgumentException("CSV 未加引号字段中包含非法引号。", nameof(csv));
                quoted = true;
                continue;
            }
            if (character == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                afterQuote = false;
                continue;
            }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                // Ignore physically blank lines, including trailing blank lines. A line
                // containing delimiters or an explicitly quoted empty field is still a record.
                var hasRecordContent = row.Count > 0 || cell.Length > 0 || afterQuote;
                if (hasRecordContent)
                {
                    row.Add(cell.ToString());
                    records.Add(row);
                }
                cell.Clear();
                afterQuote = false;
                row = [];
                if (records.Count > MaxRows + 1)
                    throw new ArgumentException($"CSV 数据行数不能超过 {MaxRows}。", nameof(csv));
                continue;
            }
            if (afterQuote && char.IsWhiteSpace(character))
                continue;
            cell.Append(character);
        }

        if (quoted)
            throw new ArgumentException("CSV 包含未闭合的引号。", nameof(csv));
        if (cell.Length > 0 || row.Count > 0 || afterQuote)
        {
            // An explicitly quoted empty final field is a real CSV record even without a trailing newline.
            row.Add(cell.ToString());
            records.Add(row);
        }
        return records;
    }
}

public sealed record CrmCsvImportPreviewRow(
    int SourceRowNumber,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> Errors);

public sealed record CrmCsvImportPreviewResult(
    IReadOnlyList<string> Headers,
    IReadOnlyDictionary<string, string> FieldMapping,
    IReadOnlyList<string> MissingRequiredFields,
    int TotalRows,
    int InvalidRows,
    int ValidRows,
    IReadOnlyList<CrmCsvImportPreviewRow> PreviewRows);
