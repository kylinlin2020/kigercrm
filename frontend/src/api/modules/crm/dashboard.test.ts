import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmDashboardApi } from './dashboard'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmDashboardApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue({})
  })

  it('routes the new read-only reports with an optional UTC range', async () => {
    await crmDashboardApi.teamRanking('2026-11-01T00:00:00Z', '2026-11-30T00:00:00Z')
    await crmDashboardApi.followUpHealth()

    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/TeamRanking', {
      params: { fromUtc: '2026-11-01T00:00:00Z', toUtc: '2026-11-30T00:00:00Z' },
    })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/FollowUpHealth', {
      params: { fromUtc: undefined, toUtc: undefined },
    })
  })

  it('keeps all report methods read-only GET calls', () => {
    expect(requestMocks.post).not.toHaveBeenCalled()
  })
})
