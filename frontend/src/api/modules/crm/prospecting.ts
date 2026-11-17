import type { ApiId, PageResult } from '../../types'
import type { CrmLeadDetailDto } from './lead.types'
import type {
  CrmProspectingBulkResultDto,
  CrmProspectingCampaignDto,
  CrmProspectingCampaignInputDto,
  CrmProspectingCandidateBulkDto,
  CrmProspectingCandidateBulkReviewDto,
  CrmProspectingCandidateDto,
  CrmProspectingCandidatePageQueryDto,
  CrmProspectingCandidateReviewDetailDto,
  CrmProspectingCandidateReviewDto,
  CrmProspectingProviderCatalogDto,
  CrmProspectingRunDto,
  CrmProspectingRunMonitorDto,
  CrmProspectingRunStartDto,
  CrmProspectingStrategyDecisionDto,
  CrmProspectingStrategyDto,
  CrmProspectingStrategyInputDto,
  CrmProspectingSuperSearchPreviewDto,
  CrmProspectingSuperSearchPreviewInputDto,
} from './prospecting.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmProspecting')
// Read-side catalog lives in its own DynamicApi service (QueryServices convention).
const catalogApi = createDynamicApiClient('CrmProspectingProviderCatalogQuery')
// Run monitor (progress + provider execution ledger) follows the same QueryServices convention.
const runQueryApi = createDynamicApiClient('CrmProspectingRunQuery')
// Paginated candidate review queue; GetPageAsync routes to Page (see crud-client notes).
const candidateQueryApi = createDynamicApiClient('CrmProspectingCandidateQuery')
// Read-only deterministic Super Search planner preview (no provider calls, no writes, no Run).
const superSearchQueryApi = createDynamicApiClient('CrmProspectingSuperSearchQuery')

export const crmProspectingApi = {
  providerCatalog() {
    return catalogApi.get<CrmProspectingProviderCatalogDto>('Providers')
  },
  campaigns() {
    return api.get<CrmProspectingCampaignDto[]>('Campaigns')
  },
  campaign(id: ApiId) {
    return api.get<CrmProspectingCampaignDto | null>('Campaign', { id })
  },
  createCampaign(input: CrmProspectingCampaignInputDto) {
    return api.post<CrmProspectingCampaignDto, CrmProspectingCampaignInputDto>('Campaign', input)
  },
  updateCampaign(input: CrmProspectingCampaignInputDto) {
    return api.put<CrmProspectingCampaignDto, CrmProspectingCampaignInputDto>('Campaign', input)
  },
  strategies(campaignId: ApiId) {
    return api.get<CrmProspectingStrategyDto[]>('Strategies', { campaignId })
  },
  createStrategyVersion(input: CrmProspectingStrategyInputDto) {
    return api.post<CrmProspectingStrategyDto, CrmProspectingStrategyInputDto>('StrategyVersion', input)
  },
  submitStrategyForApproval(strategyId: ApiId) {
    return api.post<CrmProspectingStrategyDto, undefined>('SubmitStrategyForApproval', undefined, { params: { strategyId } })
  },
  decideStrategy(input: CrmProspectingStrategyDecisionDto) {
    return api.post<CrmProspectingStrategyDto, CrmProspectingStrategyDecisionDto>('DecideStrategy', input)
  },
  activateStrategy(strategyId: ApiId) {
    return api.post<CrmProspectingCampaignDto, undefined>('ActivateStrategy', undefined, { params: { strategyId } })
  },
  runs(campaignId: ApiId) {
    return api.get<CrmProspectingRunDto[]>('CampaignRuns', { campaignId })
  },
  startRun(input: CrmProspectingRunStartDto) {
    return api.post<CrmProspectingRunDto, CrmProspectingRunStartDto>('StartRun', input)
  },
  run(runId: ApiId) {
    return api.get<CrmProspectingRunDto | null>('Run', { runId })
  },
  runMonitor(runId: ApiId) {
    return runQueryApi.get<CrmProspectingRunMonitorDto>('RunMonitor', { runId })
  },
  cancelRun(runId: ApiId) {
    return api.post<CrmProspectingRunDto, undefined>('CancelRun', undefined, { params: { runId } })
  },
  pauseRun(runId: ApiId) {
    return api.post<CrmProspectingRunDto, undefined>('PauseRun', undefined, { params: { runId } })
  },
  resumeRun(runId: ApiId) {
    return api.post<CrmProspectingRunDto, undefined>('ResumeRun', undefined, { params: { runId } })
  },
  candidatePage(input: CrmProspectingCandidatePageQueryDto) {
    return candidateQueryApi.post<PageResult<CrmProspectingCandidateDto>, CrmProspectingCandidatePageQueryDto>('Page', input)
  },
  candidateReviewDetail(candidateId: ApiId) {
    return api.get<CrmProspectingCandidateReviewDetailDto>('CandidateReviewDetail', { candidateId })
  },
  reviewCandidate(input: CrmProspectingCandidateReviewDto) {
    return api.post<CrmProspectingCandidateDto, CrmProspectingCandidateReviewDto>('ReviewCandidate', input)
  },
  convertCandidate(candidateId: ApiId) {
    return api.post<CrmLeadDetailDto, undefined>('ConvertApprovedCandidateToLead', undefined, { params: { candidateId } })
  },
  bulkReviewCandidates(input: CrmProspectingCandidateBulkReviewDto) {
    return api.post<CrmProspectingBulkResultDto, CrmProspectingCandidateBulkReviewDto>('BulkReviewCandidates', input)
  },
  bulkConvertCandidates(input: CrmProspectingCandidateBulkDto) {
    return api.post<CrmProspectingBulkResultDto, CrmProspectingCandidateBulkDto>('BulkConvertCandidates', input)
  },
  shortlistCandidates(input: CrmProspectingCandidateBulkDto) {
    return api.post<CrmProspectingBulkResultDto, CrmProspectingCandidateBulkDto>('ShortlistCandidates', input)
  },
  unshortlistCandidates(input: CrmProspectingCandidateBulkDto) {
    return api.post<CrmProspectingBulkResultDto, CrmProspectingCandidateBulkDto>('UnshortlistCandidates', input)
  },
  previewSuperSearch(input: CrmProspectingSuperSearchPreviewInputDto) {
    return superSearchQueryApi.post<CrmProspectingSuperSearchPreviewDto, CrmProspectingSuperSearchPreviewInputDto>('PreviewSuperSearch', input)
  },
}
