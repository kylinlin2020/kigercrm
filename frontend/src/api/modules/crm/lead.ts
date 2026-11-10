import type { ApiId } from '../../types'
import type {
  CrmLeadCreateDto,
  CrmLeadDetailDto,
  CrmLeadDuplicateCandidateDto,
  CrmLeadDuplicateCheckDto,
  CrmLeadListItemDto,
  CrmLeadPageQueryDto,
  CrmLeadUpdateDto,
} from './lead.types'
import {
  createDynamicApiClient,
} from '../../base'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const leadReadApi = createCrmReadClient<CrmLeadListItemDto, CrmLeadDetailDto, CrmLeadPageQueryDto>('CrmLeadQuery')
const leadCommandApi = createCrmCommandClient<CrmLeadCreateDto, CrmLeadUpdateDto, CrmLeadDetailDto>('CrmLead')
const leadCommandRouteApi = createDynamicApiClient('CrmLead')

const duplicateQueryApi = createDynamicApiClient('CrmLeadDuplicateQuery')

export const crmLeadApi = {
  create(input: CrmLeadCreateDto) {
    return leadCommandApi.create(input)
  },
  update(input: CrmLeadUpdateDto) {
    return leadCommandApi.update(input)
  },
  delete(id: ApiId) {
    return leadCommandApi.delete(id)
  },
  convert(id: ApiId) {
    return leadCommandApiAction<CrmLeadDetailDto>('Convert', { id })
  },
  detail(id: ApiId) {
    return leadReadApi.detail(id)
  },
  page(input: CrmLeadPageQueryDto) {
    return leadReadApi.page(input)
  },
  findPotentialDuplicates(input: CrmLeadDuplicateCheckDto) {
    return duplicateQueryApi.post<CrmLeadDuplicateCandidateDto[], CrmLeadDuplicateCheckDto>('PotentialDuplicates', input)
  },
}

function leadCommandApiAction<TResult>(action: string, params: { id: ApiId }) {
  return leadCommandRouteApi.post<TResult, undefined>(action, undefined, { params })
}
