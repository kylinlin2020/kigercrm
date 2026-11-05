import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({
  requestClient: requestMocks,
}))

import { crmOpportunityApi } from './opportunity'

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
    await crmOpportunityApi.detail(42)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOpportunityQuery/CrmOpportunityPage', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOpportunityQuery/CrmOpportunityDetail', expect.objectContaining({
      params: { id: 42 },
    }))
  })

  it('uses the Opportunity command controller for create, update, and delete', async () => {
    const create = { customerId: '9', name: 'Expansion', stage: 0, amount: 1000, currency: 'USD' }

    await crmOpportunityApi.create(create)
    await crmOpportunityApi.update({ ...create, basicId: '42' } as never)
    await crmOpportunityApi.delete('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOpportunity/CrmOpportunity', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmOpportunity/CrmOpportunity', expect.objectContaining({
      basicId: '42',
      name: 'Expansion',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmOpportunity/CrmOpportunity', expect.objectContaining({
      params: { id: '42' },
    }))
  })
})
