import type { ApiId } from '../../types'
import type {
  CrmOpportunityCreateDto,
  CrmOpportunityDetailDto,
  CrmOpportunityListItemDto,
  CrmOpportunityPageQueryDto,
  CrmOpportunityUpdateDto,
} from './opportunity.types'
import { createCommandApi, createDynamicApiClient, createReadApi } from '../../base'

const opportunityReadApi = createReadApi<CrmOpportunityListItemDto, CrmOpportunityDetailDto, CrmOpportunityPageQueryDto>(
  'CrmOpportunityQuery',
  'CrmOpportunity',
)
const opportunityCommandApi = createCommandApi<CrmOpportunityCreateDto, CrmOpportunityUpdateDto, CrmOpportunityDetailDto>(
  'CrmOpportunity',
  'CrmOpportunity',
)
const opportunityCommandRouteApi = createDynamicApiClient('CrmOpportunity')

export const crmOpportunityApi = {
  create(input: CrmOpportunityCreateDto) {
    return opportunityCommandApi.create(input)
  },
  update(input: CrmOpportunityUpdateDto) {
    return opportunityCommandApi.update(input)
  },
  delete(id: ApiId) {
    return opportunityCommandRouteApi.delete<void>('CrmOpportunity', { id })
  },
  detail(id: ApiId) {
    return opportunityReadApi.detail(id)
  },
  page(input: CrmOpportunityPageQueryDto) {
    return opportunityReadApi.page(input)
  },
}
