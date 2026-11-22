import type { ApiId } from '../../types'
import type {
  CrmCustomerPoolActionDto,
  CrmCustomerPoolAddDto,
  CrmCustomerPoolEntryDto,
} from './customer-pool.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmCustomerPool')

/** Customer shared pool: add, claim, release, assign and recover idle claims. */
export const crmCustomerPoolApi = {
  add(input: CrmCustomerPoolAddDto) {
    return api.post<CrmCustomerPoolEntryDto, CrmCustomerPoolAddDto>('Add', input)
  },
  available() {
    return api.get<CrmCustomerPoolEntryDto[]>('Available')
  },
  mine() {
    return api.get<CrmCustomerPoolEntryDto[]>('Mine')
  },
  claim(input: CrmCustomerPoolActionDto) {
    return api.post<CrmCustomerPoolEntryDto, CrmCustomerPoolActionDto>('Claim', input)
  },
  release(input: CrmCustomerPoolActionDto) {
    return api.post<CrmCustomerPoolEntryDto, CrmCustomerPoolActionDto>('Release', input)
  },
  assign(entryId: ApiId, userId: ApiId) {
    return api.post<CrmCustomerPoolEntryDto, undefined>('Assign', undefined, {
      params: { entryId, userId },
    })
  },
  recoverIdle() {
    return api.post<number, undefined>('RecoverIdle')
  },
}
