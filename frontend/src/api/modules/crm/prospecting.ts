import type { ApiId } from '../../types'
import type {
  CrmProspectingCampaignDto,
  CrmProspectingCampaignInputDto,
  CrmProspectingCandidateDto,
  CrmProspectingCandidateReviewDto,
  CrmProspectingRunDto,
  CrmProspectingRunStartDto,
  CrmProspectingStrategyDecisionDto,
  CrmProspectingStrategyDto,
  CrmProspectingStrategyInputDto,
} from './prospecting.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmProspecting')

export const crmProspectingApi = {
  campaigns() {
    return api.get<CrmProspectingCampaignDto[]>('GetCampaigns')
  },
  campaign(id: ApiId) {
    return api.get<CrmProspectingCampaignDto | null>('GetCampaign', { id })
  },
  createCampaign(input: CrmProspectingCampaignInputDto) {
    return api.post<CrmProspectingCampaignDto, CrmProspectingCampaignInputDto>('CreateCampaign', input)
  },
  updateCampaign(input: CrmProspectingCampaignInputDto) {
    return api.post<CrmProspectingCampaignDto, CrmProspectingCampaignInputDto>('UpdateCampaign', input)
  },
  strategies(campaignId: ApiId) {
    return api.get<CrmProspectingStrategyDto[]>('GetStrategies', { campaignId })
  },
  createStrategyVersion(input: CrmProspectingStrategyInputDto) {
    return api.post<CrmProspectingStrategyDto, CrmProspectingStrategyInputDto>('CreateStrategyVersion', input)
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
    return api.get<CrmProspectingRunDto[]>('GetCampaignRuns', { campaignId })
  },
  startRun(input: CrmProspectingRunStartDto) {
    return api.post<CrmProspectingRunDto, CrmProspectingRunStartDto>('StartRun', input)
  },
  run(runId: ApiId) {
    return api.get<CrmProspectingRunDto | null>('GetRun', { runId })
  },
  cancelRun(runId: ApiId) {
    return api.post<CrmProspectingRunDto, undefined>('CancelRun', undefined, { params: { runId } })
  },
  candidates(runId: ApiId, status?: number) {
    return api.get<CrmProspectingCandidateDto[]>('GetRunCandidates', { runId, status })
  },
  reviewCandidate(input: CrmProspectingCandidateReviewDto) {
    return api.post<CrmProspectingCandidateDto, CrmProspectingCandidateReviewDto>('ReviewCandidate', input)
  },
  convertCandidate(candidateId: ApiId) {
    return api.post<unknown, undefined>('ConvertApprovedCandidateToLead', undefined, { params: { candidateId } })
  },
}
