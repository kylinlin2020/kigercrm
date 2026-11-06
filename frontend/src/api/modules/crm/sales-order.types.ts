import type { ApiId, BasicDto, DateTimeString, PageRequest } from '../../types'

export enum CrmSalesOrderStatus {
  Draft = 0,
  Confirmed = 1,
  Processing = 2,
  PartiallyShipped = 3,
  Completed = 4,
  Cancelled = 5,
}

export interface CrmSalesOrderPageQueryDto extends PageRequest {
  keyword?: string | null
  customerId?: ApiId | null
  status?: CrmSalesOrderStatus | null
  ownerUserId?: ApiId | null
}

export interface CrmSalesOrderListItemDto extends BasicDto {
  orderNumber: string
  sourceQuoteId?: ApiId | null
  customerId: ApiId
  customerNameSnapshot: string
  status: CrmSalesOrderStatus
  orderDate: DateTimeString
  currency: string
  grandTotal: number
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmSalesOrderDetailDto extends CrmSalesOrderListItemDto {
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  subtotal: number
  discountTotal: number
  taxTotal: number
  customerPurchaseOrderNumber?: string | null
  paymentTerms?: string | null
  deliveryTerms?: string | null
  termsAndConditions?: string | null
  notes?: string | null
}

export interface CrmSalesOrderLinePageQueryDto extends PageRequest {
  salesOrderId: ApiId
}

export interface CrmSalesOrderLineDto extends BasicDto {
  sourceQuoteLineId?: ApiId | null
  productCode?: string | null
  description: string
  quantity: number
  reservedQuantity: number
  shippedQuantity: number
  remainingQuantity: number
  unit?: string | null
  unitPrice: number
  discountRate: number
  taxRate: number
  lineSubtotal: number
  lineDiscount: number
  lineTax: number
  lineTotal: number
  sortOrder: number
}

export interface CrmSalesOrderStatusUpdateDto {
  id: ApiId
  status: CrmSalesOrderStatus
}

export interface CrmSalesOrderLineCreateDto {
  productCode?: string | null
  description: string
  quantity: number
  unit?: string | null
  unitPrice: number
  discountRate: number
  taxRate: number
  sortOrder: number
}

export interface CrmSalesOrderCreateDto {
  orderNumber: string
  /** Stable key reused when retrying the same logical create request. */
  numberingIdempotencyKey?: string
  customerId: ApiId
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  orderDate?: DateTimeString
  currency: string
  ownerUserId?: ApiId | null
  customerPurchaseOrderNumber?: string | null
  paymentTerms?: string | null
  deliveryTerms?: string | null
  notes?: string | null
  lines: CrmSalesOrderLineCreateDto[]
}


export interface CrmSalesOrderLineUpdateDto extends CrmSalesOrderLineCreateDto {
  id: ApiId
  sourceQuoteLineId?: ApiId | null
}

export interface CrmSalesOrderUpdateDto {
  id: ApiId
  customerId: ApiId
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  orderDate?: DateTimeString
  currency: string
  customerPurchaseOrderNumber?: string | null
  paymentTerms?: string | null
  deliveryTerms?: string | null
  notes?: string | null
  lines: CrmSalesOrderLineUpdateDto[]
}
