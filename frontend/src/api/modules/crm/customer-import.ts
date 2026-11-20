import type {
  CrmCustomerImportInputDto,
  CrmCustomerImportPreviewDto,
  CrmCustomerImportResultDto,
} from './customer-import.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmCustomerImport')

/** CRM customer import: field mapping, zero-write preview and audited creation. */
export const crmCustomerImportApi = {
  preview(input: CrmCustomerImportInputDto) {
    return api.post<CrmCustomerImportPreviewDto, CrmCustomerImportInputDto>('Preview', input)
  },
  import(input: CrmCustomerImportInputDto) {
    return api.post<CrmCustomerImportResultDto, CrmCustomerImportInputDto>('Import', input)
  },
}
