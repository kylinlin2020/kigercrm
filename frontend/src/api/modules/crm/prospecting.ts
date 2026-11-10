import type { ApiId } from '../../types'
import type { CrmLeadDetailDto } from './lead.types'
import type {
  CrmProspectingCampaignDto,
  CrmProspectingCampaignInputDto,
  CrmProspectingCandidateDto,
  CrmProspectingCandidateReviewDetailDto,
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
  cancelRun(runId: ApiId) {
    return api.post<CrmProspectingRunDto, undefined>('CancelRun', undefined, { params: { runId } })
  },
  candidates(runId: ApiId, status?: number) {
    return api.get<CrmProspectingCandidateDto[]>('RunCandidates', { runId, status })
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
}
