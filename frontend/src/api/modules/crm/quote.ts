import type { ApiId, PageResult } from '../../types'
import type {
  CrmQuoteCreateDto,
  CrmQuoteDetailDto,
  CrmQuoteLineDto,
  CrmQuoteLinePageQueryDto,
  CrmQuoteListItemDto,
  CrmQuotePageQueryDto,
  CrmQuoteStatusUpdateDto,
} from './quote.types'
import { createDynamicApiClient } from '../../base'

const quoteQueryApi = createDynamicApiClient('CrmQuoteQuery')
const quoteCommandApi = createDynamicApiClient('CrmQuote')

export const crmQuoteApi = {
  create(input: CrmQuoteCreateDto) {
    return quoteCommandApi.post<CrmQuoteDetailDto, CrmQuoteCreateDto>('CrmQuote', input)
  },
  revise(sourceQuoteId: ApiId, input: CrmQuoteCreateDto) {
    return quoteCommandApi.post<CrmQuoteDetailDto, CrmQuoteCreateDto>('Revise', input, { params: { sourceQuoteId } })
  },
  changeStatus(input: CrmQuoteStatusUpdateDto) {
    return quoteCommandApi.post<CrmQuoteDetailDto, CrmQuoteStatusUpdateDto>('ChangeStatus', input)
  },
  page(input: CrmQuotePageQueryDto) {
    return quoteQueryApi.post<PageResult<CrmQuoteListItemDto>, CrmQuotePageQueryDto>('CrmQuotePage', input)
  },
  detail(id: ApiId) {
    return quoteQueryApi.get<CrmQuoteDetailDto | null>('CrmQuoteDetail', { id })
  },
  lines(input: CrmQuoteLinePageQueryDto) {
    return quoteQueryApi.post<PageResult<CrmQuoteLineDto>, CrmQuoteLinePageQueryDto>('CrmQuoteLinesPage', input)
  },
}
