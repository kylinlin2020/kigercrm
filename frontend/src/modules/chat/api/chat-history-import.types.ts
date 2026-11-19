import type { ApiId, DateTimeString } from '@/api/types'

export interface ChatHistoryImportRowInput {
  externalId: string
  sender: string
  recipient: string
  content: string
  sentAt: string
}

export interface ChatHistoryImportInput {
  fileName: string
  sourceChannel: string
  rows: ChatHistoryImportRowInput[]
}

export interface ChatHistoryImportVerdict {
  externalId: string
  status: number
  reason?: string | null
  sentAtUtc?: DateTimeString | null
  contentHash: string
}

export interface ChatHistoryImportResult {
  batchId: ApiId
  total: number
  imported: number
  duplicates: number
  invalid: number
  rows: ChatHistoryImportVerdict[]
}

export interface ChatHistoryImportBatch {
  basicId: ApiId
  fileName: string
  sourceChannel: string
  totalCount: number
  importedCount: number
  duplicateCount: number
  invalidCount: number
  status: number
  createdTime: DateTimeString
}
