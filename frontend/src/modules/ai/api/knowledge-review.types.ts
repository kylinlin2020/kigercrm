import type { ApiId, DateTimeString } from '@/api/types'

export interface KnowledgeReviewDto {
  basicId: ApiId
  documentId: ApiId
  status: number
  versionHash: string
  comment?: string | null
  reviewerId?: ApiId | null
  reviewedAt?: DateTimeString | null
  createdTime: DateTimeString
}

export interface KnowledgeReviewDecisionDto {
  reviewId: ApiId
  approve: boolean
  comment?: string | null
}
