import type { ApiId, DateTimeString } from '../../types'

export enum CrmCustomerPoolStatus {
  Available = 0,
  Claimed = 1,
  Recovered = 2,
}

export interface CrmCustomerPoolEntryDto {
  basicId: ApiId
  customerId: ApiId
  customerName: string
  status: CrmCustomerPoolStatus
  claimedUserId?: ApiId | null
  claimedAtUtc?: DateTimeString | null
  claimCount: number
  lastReleasedAtUtc?: DateTimeString | null
}

export interface CrmCustomerPoolAddDto {
  customerId: ApiId
  note?: string | null
}

export interface CrmCustomerPoolActionDto {
  entryId: ApiId
}
