import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

import { crmQuoteApi } from './quote'

describe('crmQuoteApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
  })

  it('uses quote query routes for page, detail and lines', async () => {
    const page = { page: { pageIndex: 1, pageSize: 20 }, keyword: 'Q-100' } as never
    const lines = { page: { pageIndex: 1, pageSize: 20 }, quoteId: '9' } as never
    await crmQuoteApi.page(page)
    await crmQuoteApi.detail('9')
    await crmQuoteApi.lines(lines)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmQuoteQuery/CrmQuotePage', page, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmQuoteQuery/CrmQuoteDetail', expect.objectContaining({ params: { id: '9' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmQuoteQuery/CrmQuoteLinesPage', lines, undefined)
  })

  it('uses quote command routes for create, revision and status changes', async () => {
    const input = { quoteNumber: 'Q-100', customerId: '5', lines: [] } as never
    await crmQuoteApi.create(input)
    await crmQuoteApi.revise('9', input)
    await crmQuoteApi.changeStatus({ id: '9', status: 1 } as never)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmQuote/CrmQuote', input, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmQuote/Revise', input, expect.objectContaining({ params: { sourceQuoteId: '9' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmQuote/ChangeStatus', { id: '9', status: 1 }, undefined)
  })
})
