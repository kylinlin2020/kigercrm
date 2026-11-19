import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmDashboardApi } from './dashboard'
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

  it('posts objective parsing to the read-only Super Search QueryService', async () => {
    const input = { objective: '产品：泵\n市场：德国\n数据源：provider-a' }

    await crmProspectingApi.parseSuperSearchObjective(input)

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmProspectingSuperSearchQuery/ParseSuperSearchObjective',
      input,
      undefined,
    )
  })

  it('routes read-only dashboard reports through the dashboard QueryService', async () => {
    await crmDashboardApi.overview()
    await crmDashboardApi.leadFunnel()
    await crmDashboardApi.prospectSources()
    await crmDashboardApi.opportunityStages()

    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/Overview', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/LeadFunnel', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/ProspectSources', { params: undefined })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmDashboardQuery/OpportunityStages', { params: undefined })
  })

  it('routes human-gated outreach sequences and touchpoints through the outreach service', async () => {
    const sequence = { name: 'intro', steps: [{ channel: 0, delayDays: 0, template: 'hello' }] }
    await crmProspectingApi.createOutreachSequence(sequence)
    await crmProspectingApi.outreachSequences()
    await crmProspectingApi.activateOutreachSequence('10')
    await crmProspectingApi.pauseOutreachSequence('10')
    await crmProspectingApi.enrollForOutreach({ candidateId: '30', sequenceId: '10' })
    await crmProspectingApi.outreachEnrollments('10')
    await crmProspectingApi.outreachTouchpoints('20')
    await crmProspectingApi.approveOutreachTouchpoint('40')
    await crmProspectingApi.recordOutreachTouchpoint({ touchpointId: '40', state: 2 })
    await crmProspectingApi.suppressOutreachEnrollment('20', 'opt-out')
    await crmProspectingApi.outreachReview('10')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/Sequence', sequence, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreach/Sequences', { params: undefined })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/ActivateSequence', undefined, expect.objectContaining({ params: { sequenceId: '10' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/PauseSequence', undefined, expect.objectContaining({ params: { sequenceId: '10' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/EnrollCandidate', { candidateId: '30', sequenceId: '10' }, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreach/Enrollments', { params: { sequenceId: '10' } })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreach/Touchpoints', { params: { enrollmentId: '20' } })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/ApproveTouchpoint', undefined, expect.objectContaining({ params: { touchpointId: '40' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/RecordTouchpoint', { touchpointId: '40', state: 2 }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmOutreach/SuppressEnrollment', undefined, expect.objectContaining({ params: { enrollmentId: '20', reason: 'opt-out' } }))
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmOutreach/Review', { params: { sequenceId: '10' } })
  })

  it('routes staged identity and bounded website probe through qualification', async () => {
    const probe = { candidateId: '30', companyName: 'ACME GmbH', candidateUrl: 'https://acme.example.com' }

    await crmProspectingApi.bootstrapIdentity('ACME GmbH')
    await crmProspectingApi.probeWebsite(probe)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingQualification/BootstrapIdentity', undefined, {
      params: { companyName: 'ACME GmbH', countryCode: '' },
    })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingQualification/ProbeWebsite', probe, undefined)
  })

  it('routes four-step qualification through the read-only review boundary', async () => {
    const website = {
      companyName: 'ACME GmbH',
      candidateUrl: 'https://acme.example.com',
      sourceProviderCode: 'manual-review',
      sourceKind: 3,
    }

    await crmProspectingApi.resolveIdentity('ACME GmbH')
    await crmProspectingApi.assessWebsite(website)
    await crmProspectingApi.buildVerificationReport({ candidateId: '30' })
    await crmProspectingApi.verificationReport('30')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingQualification/ResolveIdentity', undefined, { params: { companyName: 'ACME GmbH' } })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingQualification/AssessWebsite', website, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingQualification/BuildVerificationReport', { candidateId: '30' }, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspectingQualification/VerificationReport', { params: { candidateId: '30' } })
  })

  it('routes recurring schedule lifecycle commands through the main Dynamic API service', async () => {
    const input = {
      campaignId: '10',
      name: 'daily scan',
      requestKeyPrefix: 'daily-scan',
      timeZoneId: 'UTC',
      frequency: 0,
      interval: 1,
      timeOfDayMinutes: 540,
      startDateUtc: '2026-01-01T09:00:00.000Z',
    }

    await crmProspectingApi.createSchedule(input)
    await crmProspectingApi.schedules('10')
    await crmProspectingApi.submitSchedule('20')
    await crmProspectingApi.decideSchedule({ scheduleId: '20', approve: true })
    await crmProspectingApi.pauseSchedule('20')
    await crmProspectingApi.resumeSchedule('20')
    await crmProspectingApi.cancelSchedule('20')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/Schedule', input, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspectingSchedule/Schedules', { params: { campaignId: '10' } })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/SubmitScheduleForApproval', undefined, expect.objectContaining({ params: { scheduleId: '20' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/DecideSchedule', { scheduleId: '20', approve: true }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/PauseSchedule', undefined, expect.objectContaining({ params: { scheduleId: '20' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/ResumeSchedule', undefined, expect.objectContaining({ params: { scheduleId: '20' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspectingSchedule/CancelSchedule', undefined, expect.objectContaining({ params: { scheduleId: '20' } }))
  })

  it('routes Super Search mission save/list/execute through the main Dynamic API service', async () => {
    const plan = {
      products: ['pump'],
      markets: ['DE'],
      customerTypes: ['distributor'],
      industries: [],
      providerCodes: ['provider-a'],
      excludedTerms: [],
      excludedDomains: [],
      depth: 0,
      requestedRounds: 2,
    }
    const input = { campaignId: '10', name: 'industrial plan', requestKey: 'mission-1', plan }

    await crmProspectingApi.saveSuperSearchMission(input)
    await crmProspectingApi.superSearchMissions('10')
    await crmProspectingApi.createRunFromSuperSearchMission('30')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmProspecting/SaveSuperSearchMission', input, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmProspecting/SuperSearchMissions', { params: { campaignId: '10' } })
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmProspecting/RunFromSuperSearchMission',
      undefined,
      expect.objectContaining({ params: { missionId: '30' } }),
    )
  })
})
