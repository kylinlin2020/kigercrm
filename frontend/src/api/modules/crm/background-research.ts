import type { ApiId } from '../../types'
import type {
  CrmBackgroundResearchDetailDto,
  CrmBackgroundResearchExportDto,
  CrmBackgroundResearchReportDto,
  CrmMarketOpportunityDto,
  CrmMarketOpportunityReviewDto,
} from './background-research.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmBackgroundResearch')

/** Local customer background research and market opportunity review. */
export const crmBackgroundResearchApi = {
  generate(customerId: ApiId) {
    return api.post<CrmBackgroundResearchDetailDto, undefined>('Generate', undefined, {
      params: { customerId },
    })
  },
  current(customerId: ApiId) {
    return api.get<CrmBackgroundResearchDetailDto | null>('Current', { customerId })
  },
  history(customerId: ApiId) {
    return api.get<CrmBackgroundResearchReportDto[]>('History', { customerId })
  },
  review(input: CrmMarketOpportunityReviewDto) {
    return api.post<CrmMarketOpportunityDto, CrmMarketOpportunityReviewDto>('ReviewOpportunity', input)
  },
  export(customerId: ApiId, format: string) {
    return api.get<CrmBackgroundResearchExportDto>('Export', { customerId, format })
  },
}
