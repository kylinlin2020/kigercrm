import type { ApiId, DateTimeString } from '../../types'

export enum CrmOutreachDraftStatus {
  Planned = 0,
  Approved = 1,
  Rejected = 2,
}

export interface CrmOutreachDraftDto {
  basicId: ApiId
  enrollmentId: ApiId
  candidateId: ApiId
  step: number
  channel: number
  renderedText: string
  contentHash: string
  status: CrmOutreachDraftStatus
  approvedBy?: ApiId | null
  approvedAt?: DateTimeString | null
  rejectionReason?: string | null
}

export interface CrmOutreachDraftPreviewDto {
  candidateId: ApiId
  template: string
}

export interface CrmOutreachDraftCreateDto {
  enrollmentId: ApiId
  step: number
}

export interface CrmOutreachDraftReviewDto {
  draftId: ApiId
  reason?: string | null
}
