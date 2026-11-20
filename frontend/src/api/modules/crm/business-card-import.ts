import type {
  CrmBusinessCardDecisionDto,
  CrmBusinessCardImportInputDto,
  CrmBusinessCardImportResultDto,
  CrmBusinessCardPreviewDto,
  CrmBusinessCardRowDto,
} from './business-card-import.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmBusinessCardImport')

/** Operator-reviewed business-card OCR import; preview is pure validation with zero writes. */
export const crmBusinessCardImportApi = {
  preview(input: CrmBusinessCardImportInputDto) {
    return api.post<CrmBusinessCardPreviewDto, CrmBusinessCardImportInputDto>('Preview', input)
  },
  import(input: CrmBusinessCardImportInputDto) {
    return api.post<CrmBusinessCardImportResultDto, CrmBusinessCardImportInputDto>('Import', input)
  },
  confirm(input: CrmBusinessCardDecisionDto) {
    return api.post<CrmBusinessCardRowDto, CrmBusinessCardDecisionDto>('Confirm', input)
  },
  reject(input: CrmBusinessCardDecisionDto) {
    return api.post<CrmBusinessCardRowDto, CrmBusinessCardDecisionDto>('Reject', input)
  },
  rows(batchId: string) {
    return api.get<CrmBusinessCardRowDto[]>('Rows', { batchId })
  },
  batches() {
    return api.get<string[]>('Batches')
  },
}
