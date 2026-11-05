import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

export interface CrmContactPageQueryDto extends PageRequest {
  customerId?: ApiId | null
  keyword?: string | null
  ownerUserId?: ApiId | null
}

export interface CrmContactListItemDto extends BasicDto {
  customerId: ApiId
  fullName: string
  jobTitle?: string | null
  email?: string | null
  phone?: string | null
  isPrimary: boolean
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmContactDetailDto extends BasicDto {
  customerId: ApiId
  firstName: string
  lastName: string
  jobTitle?: string | null
  email?: string | null
  phone?: string | null
  whatsApp?: string | null
  ownerUserId?: ApiId | null
  isPrimary: boolean
  notes?: string | null
}

export interface CrmContactCreateDto {
  customerId: ApiId
  firstName: string
  lastName: string
  jobTitle?: string | null
  email?: string | null
  phone?: string | null
  whatsApp?: string | null
  ownerUserId?: ApiId | null
  isPrimary: boolean
  notes?: string | null
}

export interface CrmContactUpdateDto extends BasicUpdateDto {
  customerId: ApiId
  firstName: string
  lastName: string
  jobTitle?: string | null
  email?: string | null
  phone?: string | null
  whatsApp?: string | null
  ownerUserId?: ApiId | null
  isPrimary: boolean
  notes?: string | null
}
