import type { ApiId } from '@/api/types'

export interface KnowledgeAccessDto {
  documentId: ApiId
  visibility: number
  allowedRoles: string[]
}

export interface KnowledgeAccessUpdateDto {
  documentId: ApiId
  visibility: number
  allowedRoles: string[]
}
