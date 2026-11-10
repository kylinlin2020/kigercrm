import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmOpportunityApi } from './opportunity'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({
  requestClient: requestMocks,
}))

describe('crmOpportunityApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses the shared Opportunity read routes', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, keyword: 'Expansion' } as never

    await crmOpportunityApi.page(query)
    await crmOpportunityApi.detail('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOpportunityQuery/Page', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOpportunityQuery/Detail', expect.objectContaining({
      params: { id: '42' },
    }))
  })

  it('uses the Opportunity command controller for create, update, and delete', async () => {
    const create = { customerId: '9', name: 'Expansion', stage: 0, amount: 1000, currency: 'USD' }

    await crmOpportunityApi.create(create)
    await crmOpportunityApi.update({ ...create, basicId: '42' } as never)
    await crmOpportunityApi.delete('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOpportunity/Create', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmOpportunity/Update', expect.objectContaining({
      basicId: '42',
      name: 'Expansion',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmOpportunity/Delete', expect.objectContaining({
      params: { id: '42' },
    }))
  })
})
