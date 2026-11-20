// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Customs utility: deterministic validation, filtering and injection-safe CSV export.</summary>
public sealed class CrmCustomsExportTests
{
    private const string Payload = """
        [
          {"companyName":"Beta GmbH","countryCode":"de","hsCode":"841370","industry":"Pumps","exportDate":"2026-11-20","amount":1200,"productDescription":"=cmd"},
          {"companyName":"Alpha Ltd","countryCode":"CN","hsCode":"8413701090","industry":"Pumps","exportDate":"2026-11-19","amount":800},
          {"companyName":"Gamma","countryCode":"us","hsCode":"123","industry":"Other","exportDate":"nope","amount":-1}
        ]
        """;

    [Fact]
    public void Policy_ShouldValidateNormaliseAndFilterRows()
    {
        var rows = CrmCustomsExportPolicy.ParseRows(Payload);
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Count(row => row.Errors.Count == 0));
        Assert.Equal("DE", rows[0].NormalizedCountry);
        Assert.Equal("841370", rows[0].NormalizedHsCode);
        Assert.Contains("invalid_hs_code", rows[2].Errors);
        Assert.Contains("invalid_export_date", rows[2].Errors);
        Assert.Contains("invalid_amount", rows[2].Errors);
        Assert.Throws<ArgumentException>(() => CrmCustomsExportPolicy.ParseRows("[]"));
        Assert.Throws<ArgumentException>(() => CrmCustomsExportPolicy.ParseRows(
            $"[{string.Join(',', Enumerable.Range(1, 1001).Select(_ => "{\"companyName\":\"x\"}"))}]"));

        var filtered = CrmCustomsExportPolicy.Filter(rows, new CrmCustomsFilter("pump", "841370", "CN"));
        var match = Assert.Single(filtered);
        Assert.Equal("Alpha Ltd", match.CompanyName);
        Assert.Equal("CN", match.NormalizedCountry);
        Assert.Empty(CrmCustomsExportPolicy.Filter(rows, new CrmCustomsFilter(null, "999999", null)));
    }

    [Fact]
    public void Csv_ShouldEscapeCellsAndBlockFormulaInjection()
    {
        var rows = CrmCustomsExportPolicy.Filter(
            CrmCustomsExportPolicy.ParseRows(Payload),
            new CrmCustomsFilter(null, null, null));
        var csv = CrmCustomsExportPolicy.ToCsv(rows);
        Assert.StartsWith("CompanyName,CountryCode,HsCode,Industry,ExportDate,Amount,ProductDescription", csv, StringComparison.Ordinal);
        Assert.Contains("\"'=cmd\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"Alpha Ltd\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\"=cmd\"", csv, StringComparison.Ordinal);
        Assert.Equal(3, csv.Split('\n').Length - 1);
    }

    [Fact]
    public void Contract_ShouldGuardReadOnlyAndNoExternalClients()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", "CrmCustomsExportQueryService.cs");
        Assert.Contains("[DynamicApi(Group = \"BasicApp.Crm\"", service, StringComparison.Ordinal);
        Assert.Contains("Tag = \"CustomsExport\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitOfWork", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Qdrant", service, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}