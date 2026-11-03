import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

/** Mirrors backend CrmLeadStatus integer values. */
export enum CrmLeadStatus {
  New = 0,
  Contacted = 1,
  Qualified = 2,
  Disqualified = 3,
  Converted = 4,
}

export interface CrmLeadPageQueryDto extends PageRequest {
  keyword?: string | null
  status?: CrmLeadStatus | null
  countryRegion?: string | null
}

export interface CrmLeadListItemDto extends BasicDto {
  companyName: string
  contactName?: string | null
  email?: string | null
  phone?: string | null
  countryRegion?: string | null
  source?: string | null
  status: CrmLeadStatus
  score: number
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmLeadDetailDto extends CrmLeadListItemDto {
  website?: string | null
  sourceReference?: string | null
  convertedCustomerId?: ApiId | null
  convertedContactId?: ApiId | null
  lastContactedAt?: DateTimeString | null
  notes?: string | null
}

export interface CrmLeadCreateDto {
  companyName: string
  contactName?: string | null
  email?: string | null
  phone?: string | null
  website?: string | null
  countryRegion?: string | null
  source?: string | null
  sourceReference?: string | null
  ownerUserId?: ApiId | null
  notes?: string | null
}

export interface CrmLeadUpdateDto extends BasicUpdateDto {
  companyName: string
  contactName?: string | null
  email?: string | null
  phone?: string | null
  website?: string | null
  countryRegion?: string | null
  source?: string | null
  sourceReference?: string | null
  status: CrmLeadStatus
  ownerUserId?: ApiId | null
  notes?: string | null
}

export interface CrmLeadDuplicateCheckDto {
  excludeLeadId?: ApiId | null
  companyName?: string | null
  email?: string | null
  phone?: string | null
  website?: string | null
}

export interface CrmLeadDuplicateCandidateDto {
  candidate: CrmLeadListItemDto
  score: number
  confidence: 'High' | 'Medium' | string
  matchedSignals: string[]
}
