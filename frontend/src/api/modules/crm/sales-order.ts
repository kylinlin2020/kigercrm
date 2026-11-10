import type { ApiId, PageResult } from '../../types'
import type {
  CrmSalesOrderCreateDto,
  CrmSalesOrderDetailDto,
  CrmSalesOrderLineDto,
  CrmSalesOrderLinePageQueryDto,
  CrmSalesOrderListItemDto,
  CrmSalesOrderPageQueryDto,
  CrmSalesOrderStatusUpdateDto,
  CrmSalesOrderUpdateDto,
} from './sales-order.types'
import { createDynamicApiClient } from '../../base'

const orderQueryApi = createDynamicApiClient('CrmSalesOrderQuery')
const orderCommandApi = createDynamicApiClient('CrmSalesOrder')

export const crmSalesOrderApi = {
  create(input: CrmSalesOrderCreateDto) {
    return orderCommandApi.post<CrmSalesOrderListItemDto, CrmSalesOrderCreateDto>('Create', input)
  },
  updateDraft(input: CrmSalesOrderUpdateDto) {
    return orderCommandApi.put<CrmSalesOrderListItemDto, CrmSalesOrderUpdateDto>('Draft', input)
  },
  page(input: CrmSalesOrderPageQueryDto) {
    return orderQueryApi.post<PageResult<CrmSalesOrderListItemDto>, CrmSalesOrderPageQueryDto>('Page', input)
  },
  detail(id: ApiId) {
    return orderQueryApi.get<CrmSalesOrderDetailDto | null>('Detail', { id })
  },
  lines(input: CrmSalesOrderLinePageQueryDto) {
    return orderQueryApi.post<PageResult<CrmSalesOrderLineDto>, CrmSalesOrderLinePageQueryDto>('LinesPage', input)
  },
  changeStatus(input: CrmSalesOrderStatusUpdateDto) {
    return orderCommandApi.post<CrmSalesOrderListItemDto, CrmSalesOrderStatusUpdateDto>('ChangeStatus', input)
  },
}
