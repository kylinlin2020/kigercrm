import type { ApiId } from '../../types'
import type {
  CrmCustomerCreateDto,
  CrmCustomerDetailDto,
  CrmCustomerListItemDto,
  CrmCustomerPageQueryDto,
  CrmCustomerUpdateDto,
} from './customer.types'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const customerQueryApi = createCrmReadClient<CrmCustomerListItemDto, CrmCustomerDetailDto, CrmCustomerPageQueryDto>('CrmCustomerQuery')
const customerCommandApi = createCrmCommandClient<CrmCustomerCreateDto, CrmCustomerUpdateDto, CrmCustomerDetailDto>('CrmCustomer')

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
    return customerQueryApi.detail(id)
  },
  page(input: CrmCustomerPageQueryDto) {
    return customerQueryApi.page(input)
  },
}
