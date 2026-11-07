using System.Text;
using XiHan.BasicApp.Crm.Application.Importing;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmCsvImportPreviewServiceTests
{
    private static readonly string[] Allowed = ["CompanyName", "Email", "Country"];
    private static readonly string[] Required = ["CompanyName", "Email"];

    [Fact]
    public void Preview_ShouldMapHeadersAndValidateRequiredFieldsWithoutWritingData()
    {
        const string csv = "Company,Email,Country\nAcme Ltd,sales@acme.test,US\n,info@example.test,DE\n";
        var result = CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            Allowed,
            Required,
            new Dictionary<string, string> { ["Company"] = "CompanyName" });

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal("Acme Ltd", result.PreviewRows[0].Values["CompanyName"]);
        Assert.Contains(result.PreviewRows[1].Errors, error => error.Contains("CompanyName", StringComparison.Ordinal));
        Assert.Empty(result.MissingRequiredFields);
    }

    [Fact]
    public void Preview_ShouldReportRequiredFieldsWithoutMatchingSourceHeaders()
    {
        const string csv = "Company,Region\nAcme,US\n";
        var result = CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            Allowed,
            Required,
            new Dictionary<string, string> { ["Company"] = "CompanyName" });

        Assert.Equal(new[] { "Email" }, result.MissingRequiredFields);
        Assert.Equal(0, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
    }

    [Fact]
    public void Preview_ShouldSupportQuotedCommasAndEscapedQuotes()
    {
        const string csv = "CompanyName,Email,Country\n\"Acme, \"\"Global\"\" Ltd\",sales@acme.test,US\n";
        var result = CrmCsvImportPreviewService.Preview(Encoding.UTF8.GetBytes(csv), Allowed, Required);

        Assert.Equal("Acme, \"Global\" Ltd", result.PreviewRows[0].Values["CompanyName"]);
        Assert.Equal(1, result.ValidRows);
    }

    [Theory]
    [InlineData("CompanyName,Email\n\"Acme,sales@acme.test\n")]
    [InlineData("CompanyName,Email\nAcme \"Ltd\",sales@acme.test\n")]
    [InlineData("CompanyName,Email\nAcme,sales@acme.test\"oops\n")]
    [InlineData("CompanyName,CompanyName\nAcme,Other\n")]
    public void Preview_ShouldRejectMalformedCsvStructure(string csv)
    {
        Assert.Throws<ArgumentException>(() =>
            CrmCsvImportPreviewService.Preview(Encoding.UTF8.GetBytes(csv), Allowed, Required));
    }

    [Fact]
    public void Preview_ShouldReportRowsWithDifferentColumnCountsAsInvalid()
    {
        const string csv = "CompanyName,Email\nAcme,sales@acme.test,Extra\n";
        var result = CrmCsvImportPreviewService.Preview(Encoding.UTF8.GetBytes(csv), Allowed, Required);
        Assert.Equal(1, result.InvalidRows);
        Assert.Contains(result.PreviewRows[0].Errors, error => error.Contains("列数不匹配", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_ShouldRejectInvalidUtf8()
    {
        var bytes = new byte[] { 0x43, 0x6f, 0x6d, 0x70, 0x61, 0x6e, 0x79, 0x4e, 0x61, 0x6d, 0x65, 0x2c, 0x45, 0x6d, 0x61, 0x69, 0x6c, 0x0a, 0xff };
        Assert.Throws<ArgumentException>(() =>
            CrmCsvImportPreviewService.Preview(bytes, Allowed, Required));
    }

    [Fact]
    public void Preview_ShouldRejectMappingToUnknownOrDuplicateTargetFields()
    {
        const string csv = "A,B\n1,2\n";
        Assert.Throws<ArgumentException>(() => CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv), Allowed, Required,
            new Dictionary<string, string> { ["A"] = "NotARealField" }));
        Assert.Throws<ArgumentException>(() => CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv), Allowed, Required,
            new Dictionary<string, string> { ["A"] = "Email", ["B"] = "Email" }));
    }

    [Fact]
    public void Preview_ShouldRejectMappingsForSourceHeadersMissingFromTheCurrentFile()
    {
        const string csv = "CompanyName,Email\\nAcme,sales@acme.test\\n";
        Assert.Throws<ArgumentException>(() => CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            Allowed,
            Required,
            new Dictionary<string, string> { ["Company"] = "CompanyName" }));
    }

    [Fact]
    public void Preview_ShouldApplyResourceSpecificFieldValidators()
    {
        const string csv = "CustomerId,FirstName,LastName\n0,Ada,Lovelace\n42,Grace,Hopper\n";
        var result = CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            new[] { "CustomerId", "FirstName", "LastName" },
            new[] { "CustomerId", "FirstName", "LastName" },
            fieldValidators: new Dictionary<string, Func<string, bool>>
            {
                ["CustomerId"] = value => long.TryParse(value, out var id) && id > 0
            });

        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Contains(result.PreviewRows[0].Errors, error => error.Contains("CustomerId", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_ShouldRejectValidatorsOutsideTheResourceAllowList()
    {
        const string csv = "CompanyName,Email\nAcme,sales@acme.test\n";
        Assert.Throws<ArgumentException>(() => CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            Allowed,
            Required,
            fieldValidators: new Dictionary<string, Func<string, bool>>
            {
                ["TenantId"] = _ => true
            }));
    }

    [Fact]
    public void Preview_ShouldRejectRowsExceedingTheMaximumColumnCount()
    {
        var headers = string.Join(",", new[] { "CompanyName" }.Concat(
            Enumerable.Range(1, CrmCsvImportPreviewService.MaxColumns - 1).Select(index => $"H{index}")));
        var row = string.Join(",", Enumerable.Repeat("x", CrmCsvImportPreviewService.MaxColumns + 1));
        var csv = $"{headers}\n{row}\n";

        Assert.Throws<ArgumentException>(() => CrmCsvImportPreviewService.Preview(
            Encoding.UTF8.GetBytes(csv),
            new[] { "CompanyName" },
            new[] { "CompanyName" }));
    }
}
