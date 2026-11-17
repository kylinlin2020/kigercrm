import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmProspectingApi } from './prospecting'
import { CrmProspectingCandidateStatus } from './prospecting.types'

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
    await crmProspectingApi.providerCatalog()
    await crmProspectingApi.campaign('10')
    await crmProspectingApi.strategies('10')
    await crmProspectingApi.runs('10')
    await crmProspectingApi.candidateReviewDetail('30')
    await crmProspectingApi.runMonitor('40')

    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Campaigns', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspectingProviderCatalogQuery/Providers', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspectingRunQuery/RunMonitor', expect.objectContaining({ params: { runId: '40' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Campaign', expect.objectContaining({ params: { id: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/Strategies', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/CampaignRuns', expect.objectContaining({ params: { campaignId: '10' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/CandidateReviewDetail', expect.objectContaining({ params: { candidateId: '30' } }))
  })

  it('pages the candidate review queue through the QueryServices Page route', async () => {
    const input = {
      runId: '20',
      status: CrmProspectingCandidateStatus.Duplicate,
      conditions: { filters: [], sorts: [] },
      page: { pageIndex: 2, pageSize: 20 },
    }

    await crmProspectingApi.candidatePage(input)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingCandidateQuery/Page', input, undefined)
    expect(requestMocks.get).not.toHaveBeenCalledWith('/CrmProspecting/RunCandidates', expect.anything())
  })

  it('uses explicit bulk command routes and keeps the shortlist filter in the page query', async () => {
    const ids = ['30', '31']
    await crmProspectingApi.bulkReviewCandidates({ candidateIds: ids, approve: true, overrideDuplicate: false })
    await crmProspectingApi.bulkConvertCandidates({ candidateIds: ids })
    await crmProspectingApi.shortlistCandidates({ candidateIds: ids })
    await crmProspectingApi.unshortlistCandidates({ candidateIds: ids })
    await crmProspectingApi.candidatePage({
      runId: '20',
      shortlisted: true,
      conditions: { filters: [], sorts: [] },
      page: { pageIndex: 1, pageSize: 20 },
    })

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/BulkReviewCandidates', { candidateIds: ids, approve: true, overrideDuplicate: false }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/BulkConvertCandidates', { candidateIds: ids }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/ShortlistCandidates', { candidateIds: ids }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/UnshortlistCandidates', { candidateIds: ids }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingCandidateQuery/Page', expect.objectContaining({ runId: '20', shortlisted: true }), undefined)
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

  it('posts the Super Search preview to its own read-only QueryService route', async () => {
    const input = {
      products: ['pump'],
      markets: ['DE'],
      customerTypes: ['distributor'],
      industries: [],
      providerCodes: ['provider-a'],
      excludedTerms: ['spam'],
      excludedDomains: [],
      depth: 0,
      requestedRounds: 2,
    }

    await crmProspectingApi.previewSuperSearch(input)

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmProspectingSuperSearchQuery/PreviewSuperSearch',
      input,
      undefined,
    )
  })
})
