// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Mappers;

/// <summary>
/// Quotes, sales orders and shipments: entity-to-DTO mappings used by field-security discovery.
/// Returning these DTOs from a mapper is what lets the response filter recognise which registered
/// entity a response DTO belongs to, so masked fields are applied on list and detail reads alike.
/// Status is cast to int (matching the query services) so the field-security rename guard treats it
/// as a derived value rather than a renamed field.
/// </summary>
public static class CrmDocumentMapper
{
    public static CrmQuoteListItemDto ToQuoteListItem(SysCrmQuote entity) => new()
    {
        BasicId = entity.BasicId,
        QuoteNumber = entity.QuoteNumber,
        Version = entity.Version,
        CustomerId = entity.CustomerId,
        CustomerNameSnapshot = entity.CustomerNameSnapshot,
        Status = (int)entity.Status,
        IssueDate = entity.IssueDate,
        ValidUntil = entity.ValidUntil,
        Currency = entity.Currency,
        GrandTotal = entity.GrandTotal,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmQuoteDetailDto ToQuoteDetail(SysCrmQuote entity) => new()
    {
        BasicId = entity.BasicId,
        QuoteNumber = entity.QuoteNumber,
        Version = entity.Version,
        CustomerId = entity.CustomerId,
        CustomerNameSnapshot = entity.CustomerNameSnapshot,
        ContactId = entity.ContactId,
        ContactNameSnapshot = entity.ContactNameSnapshot,
        OpportunityId = entity.OpportunityId,
        OpportunityNameSnapshot = entity.OpportunityNameSnapshot,
        Status = (int)entity.Status,
        IssueDate = entity.IssueDate,
        ValidUntil = entity.ValidUntil,
        Currency = entity.Currency,
        Subtotal = entity.Subtotal,
        DiscountTotal = entity.DiscountTotal,
        TaxTotal = entity.TaxTotal,
        GrandTotal = entity.GrandTotal,
        OwnerUserId = entity.OwnerUserId,
        TermsAndConditions = entity.TermsAndConditions,
        Notes = entity.Notes,
        ConvertedOrderId = entity.ConvertedOrderId,
        PreviousVersionId = entity.PreviousVersionId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmQuoteLineDto ToQuoteLine(SysCrmQuoteLine entity) => new()
    {
        BasicId = entity.BasicId,
        ProductCode = entity.ProductCode,
        Description = entity.Description,
        Quantity = entity.Quantity,
        Unit = entity.Unit,
        UnitPrice = entity.UnitPrice,
        DiscountRate = entity.DiscountRate,
        TaxRate = entity.TaxRate,
        LineSubtotal = entity.LineSubtotal,
        LineDiscount = entity.LineDiscount,
        LineTax = entity.LineTax,
        LineTotal = entity.LineTotal,
        SortOrder = entity.SortOrder
    };

    public static CrmSalesOrderListItemDto ToSalesOrderListItem(SysCrmSalesOrder entity) => new()
    {
        BasicId = entity.BasicId,
        OrderNumber = entity.OrderNumber,
        SourceQuoteId = entity.SourceQuoteId,
        CustomerId = entity.CustomerId,
        CustomerNameSnapshot = entity.CustomerNameSnapshot,
        Status = (int)entity.Status,
        OrderDate = entity.OrderDate,
        Currency = entity.Currency,
        GrandTotal = entity.GrandTotal,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmSalesOrderDetailDto ToSalesOrderDetail(SysCrmSalesOrder entity) => new()
    {
        BasicId = entity.BasicId,
        OrderNumber = entity.OrderNumber,
        SourceQuoteId = entity.SourceQuoteId,
        CustomerId = entity.CustomerId,
        CustomerNameSnapshot = entity.CustomerNameSnapshot,
        ContactId = entity.ContactId,
        OpportunityId = entity.OpportunityId,
        ContactNameSnapshot = entity.ContactNameSnapshot,
        OpportunityNameSnapshot = entity.OpportunityNameSnapshot,
        Status = (int)entity.Status,
        OrderDate = entity.OrderDate,
        Currency = entity.Currency,
        Subtotal = entity.Subtotal,
        DiscountTotal = entity.DiscountTotal,
        TaxTotal = entity.TaxTotal,
        GrandTotal = entity.GrandTotal,
        OwnerUserId = entity.OwnerUserId,
        CustomerPurchaseOrderNumber = entity.CustomerPurchaseOrderNumber,
        PaymentTerms = entity.PaymentTerms,
        DeliveryTerms = entity.DeliveryTerms,
        TermsAndConditions = entity.TermsAndConditions,
        Notes = entity.Notes,
        CreatedTime = entity.CreatedTime
    };

    public static CrmSalesOrderLineDto ToSalesOrderLine(SysCrmSalesOrderLine entity) => new()
    {
        BasicId = entity.BasicId,
        SourceQuoteLineId = entity.SourceQuoteLineId,
        ProductCode = entity.ProductCode,
        Description = entity.Description,
        Quantity = entity.Quantity,
        ReservedQuantity = entity.ReservedQuantity,
        ShippedQuantity = entity.ShippedQuantity,
        Unit = entity.Unit,
        UnitPrice = entity.UnitPrice,
        DiscountRate = entity.DiscountRate,
        TaxRate = entity.TaxRate,
        LineSubtotal = entity.LineSubtotal,
        LineDiscount = entity.LineDiscount,
        LineTax = entity.LineTax,
        LineTotal = entity.LineTotal,
        SortOrder = entity.SortOrder
    };

    public static CrmShipmentListItemDto ToShipmentListItem(SysCrmShipment entity) => new()
    {
        BasicId = entity.BasicId,
        ShipmentNumber = entity.ShipmentNumber,
        SalesOrderId = entity.SalesOrderId,
        Status = (int)entity.Status,
        ShipmentDate = entity.ShipmentDate,
        DeliveredDate = entity.DeliveredDate,
        Carrier = entity.Carrier,
        TrackingNumber = entity.TrackingNumber,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmShipmentDetailDto ToShipmentDetail(SysCrmShipment entity) => new()
    {
        BasicId = entity.BasicId,
        ShipmentNumber = entity.ShipmentNumber,
        SalesOrderId = entity.SalesOrderId,
        Status = (int)entity.Status,
        ShipmentDate = entity.ShipmentDate,
        DeliveredDate = entity.DeliveredDate,
        Carrier = entity.Carrier,
        TrackingNumber = entity.TrackingNumber,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime,
        Notes = entity.Notes
    };

    public static CrmShipmentLineDto ToShipmentLine(SysCrmShipmentLine entity) => new()
    {
        BasicId = entity.BasicId,
        SalesOrderLineId = entity.SalesOrderLineId,
        Quantity = entity.Quantity
    };
}
