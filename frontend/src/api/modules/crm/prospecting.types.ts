import type { ApiId, DateTimeString } from '../../types'

export enum CrmProspectingCampaignStatus {
  Draft = 0,
  Active = 1,
  Paused = 2,
  Completed = 3,
  Archived = 4,
}

export enum CrmProspectingStrategyApprovalStatus {
  Draft = 0,
  PendingApproval = 1,
  Approved = 2,
  Rejected = 3,
  Revoked = 4,
}

export enum CrmProspectingRunStatus {
  Queued = 0,
  Running = 1,
  Succeeded = 2,
  PartiallySucceeded = 3,
  Failed = 4,
  CancelRequested = 5,
  Cancelled = 6,
}

export enum CrmProspectingCandidateStatus {
  PendingReview = 0,
  Duplicate = 1,
  Approved = 2,
  Rejected = 3,
  Converted = 4,
}

export interface CrmProspectingCampaignInputDto {
  basicId?: ApiId | null
  name: string
  objective?: string | null
  productScope?: string | null
  targetMarkets?: string | null
  idealCustomerProfile?: string | null
  ownerUserId?: ApiId | null
}

export interface CrmProspectingCampaignDto extends CrmProspectingCampaignInputDto {
  basicId: ApiId
  status: CrmProspectingCampaignStatus
  activeStrategyVersion?: number | null
  createdTime: DateTimeString
}

export interface CrmProspectingStrategyInputDto {
  campaignId: ApiId
  name: string
  searchObjective: string
  providerPlanJson: string
  excludedDomainsJson?: string | null
}

export interface CrmProspectingStrategyDto extends CrmProspectingStrategyInputDto {
  basicId: ApiId
  version: number
  approvalStatus: CrmProspectingStrategyApprovalStatus
  approvedBy?: ApiId | null
  approvedAt?: DateTimeString | null
  activatedAt?: DateTimeString | null
}

export interface CrmProspectingStrategyDecisionDto {
  strategyId: ApiId
  approve: boolean
}

export interface CrmProspectingRunStartDto {
  campaignId: ApiId
  requestKey: string
}

export interface CrmProspectingRunDto {
  basicId: ApiId
  campaignId: ApiId
  strategyId: ApiId
  strategyVersion: number
  status: CrmProspectingRunStatus
  createdTime: DateTimeString
  startedAt?: DateTimeString | null
  completedAt?: DateTimeString | null
  rawRecordCount: number
  candidateCount: number
  failureCount: number
  failureSummary?: string | null
}

export interface CrmProspectingCandidateDto {
  basicId: ApiId
  runId: ApiId
  providerCode: string
  externalId: string
  source?: string | null
  title?: string | null
  companyName: string
  contactName?: string | null
  businessEmail?: string | null
  website?: string | null
  summary?: string | null
  status: CrmProspectingCandidateStatus
  duplicateReason?: string | null
  convertedLeadId?: ApiId | null
  createdTime: DateTimeString
}

export interface CrmProspectingCandidateReviewDto {
  candidateId: ApiId
  approve: boolean
  overrideDuplicate: boolean
}
