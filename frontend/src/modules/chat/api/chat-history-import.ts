import type {
  ChatHistoryImportBatch,
  ChatHistoryImportInput,
  ChatHistoryImportResult,
  ChatHistoryImportVerdict,
} from './chat-history-import.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('ChatHistoryImport')

/** Staged chat history import: preview is pure, import writes an auditable batch. */
export const chatHistoryImportApi = {
  preview(input: ChatHistoryImportInput) {
    return api.post<ChatHistoryImportResult, ChatHistoryImportInput>('Preview', input)
  },
  import(input: ChatHistoryImportInput) {
    return api.post<ChatHistoryImportResult, ChatHistoryImportInput>('Import', input)
  },
  batches() {
    return api.get<ChatHistoryImportBatch[]>('Batches')
  },
  rows(batchId: string) {
    return api.get<ChatHistoryImportVerdict[]>('Rows', { batchId })
  },
}
