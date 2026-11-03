import type { ApiId, PageResult } from '../../types'
import type {
  CrmCustomerCreateDto,
  CrmCustomerDetailDto,
  CrmCustomerListItemDto,
  CrmCustomerPageQueryDto,
  CrmCustomerUpdateDto,
} from './customer.types'
import { createCommandApi, createDynamicApiClient } from '../../base'

const customerQueryApi = createDynamicApiClient('CrmCustomerQuery')
const customerCommandApi = createCommandApi<CrmCustomerCreateDto, CrmCustomerUpdateDto, CrmCustomerDetailDto>('CrmCustomer', 'CrmCustomer')

export const crmCustomerApi = {
  create(input: CrmCustomerCreateDto) {
    return customerCommandApi.create(input)
  },
  update(input: CrmCustomerUpdateDto) {
    return customerCommandApi.update(input)
  },
  delete(id: ApiId) {
    return customerCommandApi.delete(id)
  },
  detail(id: ApiId) {
    return customerQueryApi.get<CrmCustomerDetailDto | null>('GetDetail', { id })
  },
  page(input: CrmCustomerPageQueryDto) {
    return customerQueryApi.post<PageResult<CrmCustomerListItemDto>, CrmCustomerPageQueryDto>('GetPage', input)
  },
}
