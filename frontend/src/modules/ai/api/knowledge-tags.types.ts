import type { ApiId } from '@/api/types'

export interface KnowledgeTagItemDto {
  tag: string
  documentCount: number
}

export interface KnowledgeDuplicateGroupDto {
  hash: string
  documentIds: ApiId[]
  titles: string[]
}

export interface KnowledgeDuplicateReportDto {
  groups: number
  duplicateDocuments: number
  items: KnowledgeDuplicateGroupDto[]
}

export interface KnowledgeTagsUpdateDto {
  documentId: ApiId
  tags: string[]
}
