import type { ApiId, PageResult } from '../../types'
import type {
  CrmSalesOrderCreateDto,
  CrmSalesOrderDetailDto,
  CrmSalesOrderLineDto,
  CrmSalesOrderLinePageQueryDto,
  CrmSalesOrderListItemDto,
  CrmSalesOrderPageQueryDto,
  CrmSalesOrderStatusUpdateDto,
} from './sales-order.types'
import { createDynamicApiClient } from '../../base'

const orderQueryApi = createDynamicApiClient('CrmSalesOrderQuery')
const orderCommandApi = createDynamicApiClient('CrmSalesOrder')

export const crmSalesOrderApi = {
  create(input: CrmSalesOrderCreateDto) {
    return orderCommandApi.post<CrmSalesOrderListItemDto, CrmSalesOrderCreateDto>('Create', input)
  },
  page(input: CrmSalesOrderPageQueryDto) {
    return orderQueryApi.post<PageResult<CrmSalesOrderListItemDto>, CrmSalesOrderPageQueryDto>('CrmSalesOrderPage', input)
  },
  detail(id: ApiId) {
    return orderQueryApi.get<CrmSalesOrderDetailDto | null>('CrmSalesOrderDetail', { id })
  },
  lines(input: CrmSalesOrderLinePageQueryDto) {
    return orderQueryApi.post<PageResult<CrmSalesOrderLineDto>, CrmSalesOrderLinePageQueryDto>('CrmSalesOrderLinesPage', input)
  },
  changeStatus(input: CrmSalesOrderStatusUpdateDto) {
    return orderCommandApi.post<CrmSalesOrderListItemDto, CrmSalesOrderStatusUpdateDto>('ChangeStatus', input)
  },
}
