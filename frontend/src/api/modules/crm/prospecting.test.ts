import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmProspectingApi } from './prospecting'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmProspectingApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('uses the Prospecting dynamic API route for campaign and history reads', async () => {
    await crmProspectingApi.campaigns()
    await crmProspectingApi.campaign('10')
    await crmProspectingApi.strategies('10')
    await crmProspectingApi.runs('10')
    await crmProspectingApi.candidates('20', 1)
    await crmProspectingApi.candidateReviewDetail('30')

    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Campaigns', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Campaign', expect.objectContaining({ params: { id: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Strategies', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/CampaignRuns', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/RunCandidates', expect.objectContaining({ params: { runId: '20', status: 1 } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/CandidateReviewDetail', expect.objectContaining({ params: { candidateId: '30' } }))
  })

  it('uses explicit command routes for approval, review and conversion', async () => {
    await crmProspectingApi.createCampaign({ name: 'Generator buyers' })
    await crmProspectingApi.startRun({ campaignId: '10', requestKey: 'run-1' })
    await crmProspectingApi.reviewCandidate({ candidateId: '30', approve: true, overrideDuplicate: false })
    await crmProspectingApi.convertCandidate('30')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/Campaign', { name: 'Generator buyers' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/StartRun', { campaignId: '10', requestKey: 'run-1' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/ReviewCandidate', { candidateId: '30', approve: true, overrideDuplicate: false }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/ConvertApprovedCandidateToLead', undefined, expect.objectContaining({ params: { candidateId: '30' } }))
  })
})
