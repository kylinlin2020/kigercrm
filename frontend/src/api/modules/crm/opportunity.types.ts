import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

export enum CrmOpportunityStage {
  Qualification = 0,
  NeedsAnalysis = 1,
  Proposal = 2,
  Negotiation = 3,
  Won = 4,
  Lost = 5,
}

export interface CrmOpportunityPageQueryDto extends PageRequest {
  keyword?: string | null
  customerId?: ApiId | null
  stage?: CrmOpportunityStage | null
  ownerUserId?: ApiId | null
}

export interface CrmOpportunityListItemDto extends BasicDto {
  customerId: ApiId
  primaryContactId?: ApiId | null
  name: string
  stage: CrmOpportunityStage
  amount: number
  currency: string
  expectedCloseDate?: DateTimeString | null
  probability?: number | null
  ownerUserId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmOpportunityDetailDto extends CrmOpportunityListItemDto {
  lostReason?: string | null
}

export interface CrmOpportunityCreateDto {
  customerId: ApiId
  primaryContactId?: ApiId | null
  name: string
  stage: CrmOpportunityStage
  amount: number
  currency: string
  expectedCloseDate?: DateTimeString | null
  probability?: number | null
  ownerUserId?: ApiId | null
  lostReason?: string | null
}

export interface CrmOpportunityUpdateDto extends BasicUpdateDto {
  customerId: ApiId
  primaryContactId?: ApiId | null
  name: string
  stage: CrmOpportunityStage
  amount: number
  currency: string
  expectedCloseDate?: DateTimeString | null
  probability?: number | null
  ownerUserId?: ApiId | null
  lostReason?: string | null
}
