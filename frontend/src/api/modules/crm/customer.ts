import type { ApiId } from '../../types'
import type {
  CrmCustomerCreateDto,
  CrmCustomerDetailDto,
  CrmCustomerListItemDto,
  CrmCustomerPageQueryDto,
  CrmCustomerUpdateDto,
} from './customer.types'
import { createCommandApi, createDynamicApiClient, createReadApi } from '../../base'

const customerQueryApi = createReadApi<CrmCustomerListItemDto, CrmCustomerDetailDto, CrmCustomerPageQueryDto>('CrmCustomerQuery', 'CrmCustomer')
const customerCommandRouteApi = createDynamicApiClient('CrmCustomer')
const customerCommandApi = createCommandApi<CrmCustomerCreateDto, CrmCustomerUpdateDto, CrmCustomerDetailDto>('CrmCustomer', 'CrmCustomer')

export const crmCustomerApi = {
  create(input: CrmCustomerCreateDto) {
    return customerCommandApi.create(input)
  },
  update(input: CrmCustomerUpdateDto) {
    return customerCommandApi.update(input)
  },
  delete(id: ApiId) {
    return customerCommandRouteApi.delete<void>('CrmCustomer', { id })
  },
  detail(id: ApiId) {
    return customerQueryApi.detail(id)
  },
  page(input: CrmCustomerPageQueryDto) {
    return customerQueryApi.page(input)
  },
}
