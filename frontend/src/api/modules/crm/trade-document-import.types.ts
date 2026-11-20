import type { ApiId } from '../../types'

export enum CrmTradeDocumentType {
  Invoice = 0,
  PackingList = 1,
  BillOfLading = 2,
}

export enum CrmTradeDocumentImportStatus {
  Pending = 0,
  Confirmed = 1,
  Rejected = 2,
}

export interface CrmTradeDocumentImportInputDto {
  rowsJson: string
  batchId?: string | null
  salesOrderId?: string | null
}

export interface CrmTradeDocumentLineDto {
  sku: string
  quantity: number
  description?: string | null
}

export interface CrmTradeDocumentLinePreviewDto extends CrmTradeDocumentLineDto {
  suggestedOrderLineId?: ApiId | null
}

export interface CrmTradeDocumentPreviewRowDto {
  externalId: string
  documentType: CrmTradeDocumentType
  documentNumber: string
  orderReference: string
  buyer?: string | null
  seller?: string | null
  carrier?: string | null
  trackingNumber?: string | null
  shipmentDate?: string | null
  currency?: string | null
  totalAmount: number
  lines: CrmTradeDocumentLinePreviewDto[]
  errors: string[]
  duplicateReason?: string | null
}

export interface CrmTradeDocumentPreviewDto {
  total: number
  valid: number
  duplicates: number
  rows: CrmTradeDocumentPreviewRowDto[]
}

export interface CrmTradeDocumentImportResultDto {
  batchId: string
  staged: number
  invalid: number
  duplicateStaged: number
}

export interface CrmTradeDocumentRowDto {
  basicId: ApiId
  batchId: string
  externalId: string
  documentType: CrmTradeDocumentType
  documentNumber: string
  orderReference: string
  carrier?: string | null
  trackingNumber?: string | null
  shipmentDate?: string | null
  currency?: string | null
  totalAmount: number
  lines: CrmTradeDocumentLineDto[]
  status: CrmTradeDocumentImportStatus
  duplicateReason?: string | null
  rejectReason?: string | null
  createdShipmentId?: ApiId | null
}

export interface CrmTradeDocumentMappedLineDto {
  orderLineId: ApiId
  quantity: number
}

export interface CrmTradeDocumentConfirmDto {
  rowId: ApiId
  salesOrderId: ApiId
  lines: CrmTradeDocumentMappedLineDto[]
}

export interface CrmTradeDocumentDecisionDto {
  rowId: ApiId
  reason?: string | null
}

export interface CrmTradeDocumentOrderLineDto {
  basicId: ApiId
  productCode?: string | null
  description: string
  availableQuantity: number
}
