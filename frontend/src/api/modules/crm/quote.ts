import type { ApiId, PageResult } from '../../types'
import type {
  CrmQuoteCommandResultDto,
  CrmQuoteCreateDto,
  CrmQuoteDetailDto,
  CrmQuoteLineDto,
  CrmQuoteLinePageQueryDto,
  CrmQuoteListItemDto,
  CrmQuotePageQueryDto,
  CrmQuoteStatusUpdateDto,
} from './quote.types'
import type { CrmSalesOrderListItemDto } from './sales-order.types'
import { createDynamicApiClient } from '../../base'

const quoteQueryApi = createDynamicApiClient('CrmQuoteQuery')
const quoteCommandApi = createDynamicApiClient('CrmQuote')

export const crmQuoteApi = {
  create(input: CrmQuoteCreateDto) {
    return quoteCommandApi.post<CrmQuoteCommandResultDto, CrmQuoteCreateDto>('Create', input)
  },
  revise(sourceQuoteId: ApiId, input: CrmQuoteCreateDto) {
    return quoteCommandApi.post<CrmQuoteDetailDto, CrmQuoteCreateDto>('Revise', input, { params: { sourceQuoteId } })
  },
  changeStatus(input: CrmQuoteStatusUpdateDto) {
    return quoteCommandApi.post<CrmQuoteCommandResultDto, CrmQuoteStatusUpdateDto>('ChangeStatus', input)
  },
  convertToSalesOrder(quoteId: ApiId) {
    return quoteCommandApi.post<CrmSalesOrderListItemDto, never>('ConvertToSalesOrder', undefined as never, { params: { quoteId } })
  },
  page(input: CrmQuotePageQueryDto) {
    return quoteQueryApi.post<PageResult<CrmQuoteListItemDto>, CrmQuotePageQueryDto>('Page', input)
  },
  detail(id: ApiId) {
    return quoteQueryApi.get<CrmQuoteDetailDto | null>('Detail', { id })
  },
  lines(input: CrmQuoteLinePageQueryDto) {
    return quoteQueryApi.post<PageResult<CrmQuoteLineDto>, CrmQuoteLinePageQueryDto>('LinesPage', input)
  },
}
