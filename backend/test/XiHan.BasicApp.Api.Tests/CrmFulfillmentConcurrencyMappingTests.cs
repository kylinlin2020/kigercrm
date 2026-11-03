using System.Reflection;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmFulfillmentConcurrencyMappingTests
{
    [Theory]
    [InlineData(typeof(SysCrmSalesOrder))]
    [InlineData(typeof(SysCrmSalesOrderLine))]
    public void FulfillmentEntities_ShouldMapRowVersionAsSqlSugarOptimisticLock(Type entityType)
    {
        var rowVersion = entityType.GetProperty(nameof(SysCrmSalesOrder.RowVersion));

        Assert.NotNull(rowVersion);
        var mapping = rowVersion.GetCustomAttribute<SugarColumn>();
        Assert.NotNull(mapping);
        Assert.Equal("Row_Version", mapping.ColumnName);
        Assert.True(mapping.IsEnableUpdateVersionValidation);
    }

    [Fact]
    public void FulfillmentRevision_ShouldRemainBusinessRevisionAndNotReplaceRowVersion()
    {
        var property = typeof(SysCrmSalesOrder).GetProperty(nameof(SysCrmSalesOrder.FulfillmentRevision));

        Assert.NotNull(property);
        var mapping = property.GetCustomAttribute<SugarColumn>();
        Assert.NotNull(mapping);
        Assert.Equal("Fulfillment_Revision", mapping.ColumnName);
        Assert.False(mapping.IsEnableUpdateVersionValidation);
    }
}
