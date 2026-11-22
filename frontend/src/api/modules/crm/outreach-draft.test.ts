import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmOutreachDraftApi } from './outreach-draft'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmOutreachDraftApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes draft management through the outreach draft service', async () => {
    await crmOutreachDraftApi.preview({ candidateId: '3', template: 'Hi {{FirstName}}' })
    await crmOutreachDraftApi.create({ enrollmentId: '7', step: 0 })
    await crmOutreachDraftApi.list('7')
    await crmOutreachDraftApi.getDraft('9')
    await crmOutreachDraftApi.approve('9')
    await crmOutreachDraftApi.reject({ draftId: '9', reason: 'tone' })

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmOutreachDraft/Preview',
      { candidateId: '3', template: 'Hi {{FirstName}}' },
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmOutreachDraft/Create',
      { enrollmentId: '7', step: 0 },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreachDraft/List', { params: { enrollmentId: '7' } })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreachDraft/GetDraft', { params: { draftId: '9' } })
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmOutreachDraft/Approve',
      undefined,
      { params: { draftId: '9' } },
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmOutreachDraft/Reject',
      { draftId: '9', reason: 'tone' },
      undefined,
    )
  })
})
