import type { ApiId } from '../../types'

export enum CrmBusinessCardImportStatus {
  Pending = 0,
  Confirmed = 1,
  Rejected = 2,
}

export interface CrmBusinessCardImportInputDto {
  rowsJson: string
  batchId?: string | null
}

export interface CrmBusinessCardPreviewRowDto {
  externalId: string
  contactName?: string | null
  companyName?: string | null
  phone?: string | null
  email?: string | null
  jobTitle?: string | null
  website?: string | null
  normalizedEmail?: string | null
  phoneDigits?: string | null
  websiteHost?: string | null
  errors: string[]
  duplicateReason?: string | null
}

export interface CrmBusinessCardPreviewDto {
  total: number
  valid: number
  duplicates: number
  rows: CrmBusinessCardPreviewRowDto[]
}

export interface CrmBusinessCardImportResultDto {
  batchId: string
  staged: number
  invalid: number
  duplicateStaged: number
}

export interface CrmBusinessCardRowDto {
  basicId: ApiId
  batchId: string
  externalId: string
  contactName?: string | null
  companyName?: string | null
  phone?: string | null
  email?: string | null
  jobTitle?: string | null
  website?: string | null
  status: CrmBusinessCardImportStatus
  duplicateReason?: string | null
  rejectReason?: string | null
  convertedLeadId?: ApiId | null
}

export interface CrmBusinessCardDecisionDto {
  rowId: ApiId
  reason?: string | null
}
