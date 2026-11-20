import type {
  CrmCustomsExportResultDto,
  CrmCustomsFilterResultDto,
  CrmCustomsInputDto,
  CrmCustomsValidationDto,
} from './customs-export.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmCustomsExportQuery')

/** Read-only customs-data utility: validate, filter and export caller-supplied rows. */
export const crmCustomsExportApi = {
  validate(input: CrmCustomsInputDto) {
    return api.post<CrmCustomsValidationDto, CrmCustomsInputDto>('Validate', input)
  },
  filter(input: CrmCustomsInputDto) {
    return api.post<CrmCustomsFilterResultDto, CrmCustomsInputDto>('Filter', input)
  },
  export(input: CrmCustomsInputDto) {
    return api.post<CrmCustomsExportResultDto, CrmCustomsInputDto>('Export', input)
  },
}
