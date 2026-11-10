import type { ApiId } from '../../types'
import type {
  CrmOpportunityCreateDto,
  CrmOpportunityDetailDto,
  CrmOpportunityListItemDto,
  CrmOpportunityPageQueryDto,
  CrmOpportunityUpdateDto,
} from './opportunity.types'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const opportunityReadApi = createCrmReadClient<CrmOpportunityListItemDto, CrmOpportunityDetailDto, CrmOpportunityPageQueryDto>('CrmOpportunityQuery')
const opportunityCommandApi = createCrmCommandClient<CrmOpportunityCreateDto, CrmOpportunityUpdateDto, CrmOpportunityDetailDto>('CrmOpportunity')

export const crmOpportunityApi = {
  create(input: CrmOpportunityCreateDto) {
    return opportunityCommandApi.create(input)
  },
  update(input: CrmOpportunityUpdateDto) {
    return opportunityCommandApi.update(input)
  },
  delete(id: ApiId) {
    return opportunityCommandApi.delete(id)
  },
  detail(id: ApiId) {
    return opportunityReadApi.detail(id)
  },
  page(input: CrmOpportunityPageQueryDto) {
    return opportunityReadApi.page(input)
  },
}
