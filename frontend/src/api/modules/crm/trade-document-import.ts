import type {
  CrmTradeDocumentConfirmDto,
  CrmTradeDocumentDecisionDto,
  CrmTradeDocumentImportInputDto,
  CrmTradeDocumentImportResultDto,
  CrmTradeDocumentOrderLineDto,
  CrmTradeDocumentPreviewDto,
  CrmTradeDocumentRowDto,
} from './trade-document-import.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmTradeDocumentImport')

/** Operator-mapped trade-document OCR import; shipment creation requires explicit confirmation. */
export const crmTradeDocumentImportApi = {
  preview(input: CrmTradeDocumentImportInputDto) {
    return api.post<CrmTradeDocumentPreviewDto, CrmTradeDocumentImportInputDto>('Preview', input)
  },
  import(input: CrmTradeDocumentImportInputDto) {
    return api.post<CrmTradeDocumentImportResultDto, CrmTradeDocumentImportInputDto>('Import', input)
  },
  confirm(input: CrmTradeDocumentConfirmDto) {
    return api.post<CrmTradeDocumentRowDto, CrmTradeDocumentConfirmDto>('Confirm', input)
  },
  reject(input: CrmTradeDocumentDecisionDto) {
    return api.post<CrmTradeDocumentRowDto, CrmTradeDocumentDecisionDto>('Reject', input)
  },
  rows(batchId: string) {
    return api.get<CrmTradeDocumentRowDto[]>('Rows', { batchId })
  },
  batches() {
    return api.get<string[]>('Batches')
  },
  orderLines(salesOrderId: string) {
    return api.get<CrmTradeDocumentOrderLineDto[]>('OrderLines', { salesOrderId })
  },
}
