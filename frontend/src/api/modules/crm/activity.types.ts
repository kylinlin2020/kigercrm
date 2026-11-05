import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

export enum CrmActivityType {
  Call = 0,
  Email = 1,
  Meeting = 2,
  Note = 3,
  WhatsApp = 4,
  Other = 5,
}

export interface CrmActivityPageQueryDto extends PageRequest {
  relatedEntityType?: string | null
  relatedEntityId?: ApiId | null
  activityType?: CrmActivityType | null
  ownerUserId?: ApiId | null
  occurredFrom?: DateTimeString | null
  occurredTo?: DateTimeString | null
  keyword?: string | null
}

export interface CrmActivityListItemDto extends BasicDto {
  relatedEntityType: string
  relatedEntityId: ApiId
  activityType: CrmActivityType
  subject: string
  occurredAt: DateTimeString
  ownerUserId?: ApiId | null
}

export interface CrmActivityDetailDto extends CrmActivityListItemDto {
  content?: string | null
  externalSystem?: string | null
  externalId?: string | null
}

export interface CrmActivityCreateDto {
  relatedEntityType: string
  relatedEntityId: ApiId
  activityType: CrmActivityType
  subject: string
  content?: string | null
  occurredAt: DateTimeString
  ownerUserId?: ApiId | null
  externalSystem?: string | null
  externalId?: string | null
}

export interface CrmActivityUpdateDto extends BasicUpdateDto {
  relatedEntityType: string
  relatedEntityId: ApiId
  activityType: CrmActivityType
  subject: string
  content?: string | null
  occurredAt: DateTimeString
  ownerUserId?: ApiId | null
  externalSystem?: string | null
  externalId?: string | null
}
