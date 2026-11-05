import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

export interface CrmCustomerPageQueryDto extends PageRequest {
  keyword?: string | null
  countryRegion?: string | null
  industry?: string | null
  ownerUserId?: ApiId | null
}

export interface CrmCustomerListItemDto extends BasicDto {
  name: string
  website?: string | null
  countryRegion?: string | null
  industry?: string | null
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmCustomerDetailDto extends CrmCustomerListItemDto {
  legalName?: string | null
  source?: string | null
  notes?: string | null
}

export interface CrmCustomerCreateDto {
  name: string
  legalName?: string | null
  website?: string | null
  countryRegion?: string | null
  industry?: string | null
  source?: string | null
  ownerUserId?: ApiId | null
  notes?: string | null
}

export interface CrmCustomerUpdateDto extends BasicUpdateDto {
  name: string
  legalName?: string | null
  website?: string | null
  countryRegion?: string | null
  industry?: string | null
  source?: string | null
  ownerUserId?: ApiId | null
  notes?: string | null
}
