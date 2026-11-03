import type { ApiId, BasicDto, DateTimeString, PageRequest } from '../../types'

export enum CrmQuoteStatus {
  Draft = 0,
  Sent = 1,
  Accepted = 2,
  Rejected = 3,
  Expired = 4,
  Converted = 5,
}

export interface CrmQuoteLineCreateDto {
  productCode?: string | null
  description: string
  quantity: number
  unit?: string | null
  unitPrice: number
  discountRate: number
  taxRate: number
  sortOrder: number
}

export interface CrmQuoteCreateDto {
  quoteNumber: string
  customerId: ApiId
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  issueDate: DateTimeString
  validUntil?: DateTimeString | null
  currency: string
  ownerUserId?: ApiId | null
  termsAndConditions?: string | null
  notes?: string | null
  lines: CrmQuoteLineCreateDto[]
}

export interface CrmQuoteStatusUpdateDto {
  id: ApiId
  status: CrmQuoteStatus
}

export interface CrmQuoteListItemDto extends BasicDto {
  quoteNumber: string
  version: number
  customerId: ApiId
  customerNameSnapshot: string
  status: CrmQuoteStatus
  issueDate: DateTimeString
  validUntil?: DateTimeString | null
  currency: string
  grandTotal: number
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmQuoteCommandResultDto extends BasicDto {
  quoteNumber: string
  version: number
  customerId: ApiId
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  customerNameSnapshot: string
  contactNameSnapshot?: string | null
  status: CrmQuoteStatus
  issueDate: DateTimeString
  validUntil?: DateTimeString | null
  currency: string
  subtotal: number
  discountTotal: number
  taxTotal: number
  grandTotal: number
  ownerUserId?: ApiId | null
}

export interface CrmQuoteDetailDto extends CrmQuoteListItemDto {
  contactId?: ApiId | null
  opportunityId?: ApiId | null
  contactNameSnapshot?: string | null
  subtotal: number
  discountTotal: number
  taxTotal: number
  termsAndConditions?: string | null
  notes?: string | null
  convertedOrderId?: ApiId | null
}

export interface CrmQuoteLinePageQueryDto extends PageRequest {
  quoteId: ApiId
}

export interface CrmQuoteLineDto extends BasicDto {
  productCode?: string | null
  description: string
  quantity: number
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

export interface CrmQuotePageQueryDto extends PageRequest {
  keyword?: string | null
  customerId?: ApiId | null
  status?: CrmQuoteStatus | null
  ownerUserId?: ApiId | null
}

export interface CrmQuoteRevisionInput {
  sourceQuoteId: ApiId
  input: CrmQuoteCreateDto
}
