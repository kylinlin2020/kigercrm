import type { ApiId, DateTimeString } from '../../types'

export enum CrmResearchSource {
  CustomerProfile = 0,
  OpenOpportunity = 1,
  SourceLead = 2,
}

export enum CrmResearchReportStatus {
  Current = 0,
  Superseded = 1,
}

export enum CrmMarketOpportunityStatus {
  Proposed = 0,
  Confirmed = 1,
  Dismissed = 2,
}

export interface CrmBackgroundResearchReportDto {
  basicId: ApiId
  customerId: ApiId
  customerName: string
  status: CrmResearchReportStatus
  generatedAtUtc: DateTimeString
  generatedByUserId?: ApiId | null
  evidenceCount: number
  opportunityCount: number
}

export interface CrmResearchEvidenceDto {
  basicId: ApiId
  reportId: ApiId
  source: CrmResearchSource
  factKey: string
  factValue: string
  sourceRef?: string | null
  confidence: number
}

export interface CrmMarketOpportunityDto {
  basicId: ApiId
  reportId: ApiId
  customerId: ApiId
  title: string
  factKey: string
  factValue: string
  priorityScore: number
  status: CrmMarketOpportunityStatus
}

export interface CrmBackgroundResearchDetailDto {
  report: CrmBackgroundResearchReportDto
  evidence: CrmResearchEvidenceDto[]
  opportunities: CrmMarketOpportunityDto[]
}

export interface CrmMarketOpportunityReviewDto {
  opportunityId: ApiId
  status: CrmMarketOpportunityStatus
}
