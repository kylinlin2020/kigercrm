import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmBackgroundResearchApi } from './background-research'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmBackgroundResearchApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({})
  })

  it('routes research through the background research service', async () => {
    await crmBackgroundResearchApi.generate('7')
    await crmBackgroundResearchApi.current('7')
    await crmBackgroundResearchApi.history('7')
    await crmBackgroundResearchApi.review({ opportunityId: '9', status: 1 })

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmBackgroundResearch/Generate',
      undefined,
      { params: { customerId: '7' } },
    )
    expect(requestMocks.get).toHaveBeenCalledWith(
      '/CrmBackgroundResearch/Current',
      { params: { customerId: '7' } },
    )
    expect(requestMocks.get).toHaveBeenCalledWith(
      '/CrmBackgroundResearch/History',
      { params: { customerId: '7' } },
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmBackgroundResearch/ReviewOpportunity',
      { opportunityId: '9', status: 1 },
      undefined,
    )
  })
})
