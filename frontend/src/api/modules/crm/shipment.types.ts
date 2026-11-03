import type { ApiId, BasicDto, DateTimeString, PageRequest } from '../../types'

export enum CrmShipmentStatus {
  Draft = 0,
  Dispatched = 1,
  Delivered = 2,
  Cancelled = 3,
}

export interface CrmShipmentPageQueryDto extends PageRequest {
  keyword?: string | null
  salesOrderId?: ApiId | null
  status?: CrmShipmentStatus | null
  ownerUserId?: ApiId | null
}

export interface CrmShipmentListItemDto extends BasicDto {
  shipmentNumber: string
  salesOrderId: ApiId
  status: CrmShipmentStatus
  shipmentDate?: DateTimeString | null
  deliveredDate?: DateTimeString | null
  carrier?: string | null
  trackingNumber?: string | null
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmShipmentCommandResultDto extends BasicDto {
  shipmentNumber: string
  salesOrderId: ApiId
  status: CrmShipmentStatus
  shipmentDate?: DateTimeString | null
  deliveredDate?: DateTimeString | null
  carrier?: string | null
  trackingNumber?: string | null
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmShipmentDetailDto extends CrmShipmentListItemDto {
  notes?: string | null
}

export interface CrmShipmentLineCreateDto {
  salesOrderLineId: ApiId
  quantity: number
}

export interface CrmShipmentCreateDto {
  shipmentNumber: string
  /** Stable key reused when retrying the same logical create request. */
  numberingIdempotencyKey?: string
  salesOrderId: ApiId
  shipmentDate?: DateTimeString | null
  carrier?: string | null
  trackingNumber?: string | null
  ownerUserId?: ApiId | null
  notes?: string | null
  lines: CrmShipmentLineCreateDto[]
}

export interface CrmShipmentStatusUpdateDto {
  id: ApiId
  status: CrmShipmentStatus
}

export interface CrmShipmentLinePageQueryDto extends PageRequest {
  shipmentId: ApiId
}

export interface CrmShipmentLineDto extends BasicDto {
  salesOrderLineId: ApiId
  quantity: number
}
