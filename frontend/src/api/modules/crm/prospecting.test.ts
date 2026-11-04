import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({ requestClient: requestMocks }))

import { crmProspectingApi } from './prospecting'

describe('crmProspectingApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('uses the Prospecting dynamic API route for campaign and history reads', async () => {
    await crmProspectingApi.campaigns()
    await crmProspectingApi.strategies('10')
    await crmProspectingApi.runs('10')
    await crmProspectingApi.candidates('20', 1)

    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/GetCampaigns', undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/GetStrategies', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/GetCampaignRuns', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/GetRunCandidates', expect.objectContaining({ params: { runId: '20', status: 1 } }))
  })

  it('uses explicit command routes for approval, review and conversion', async () => {
    await crmProspectingApi.createCampaign({ name: 'Generator buyers' })
    await crmProspectingApi.startRun({ campaignId: '10', requestKey: 'run-1' })
    await crmProspectingApi.reviewCandidate({ candidateId: '30', approve: true, overrideDuplicate: false })
    await crmProspectingApi.convertCandidate('30')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/CreateCampaign', { name: 'Generator buyers' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/StartRun', { campaignId: '10', requestKey: 'run-1' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/ReviewCandidate', { candidateId: '30', approve: true, overrideDuplicate: false }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/ConvertApprovedCandidateToLead', undefined, expect.objectContaining({ params: { candidateId: '30' } }))
  })
})
