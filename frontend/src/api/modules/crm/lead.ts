import type { ApiId, PageResult } from '../../types'
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
  createCommandApi,
  createDynamicApiClient,
} from '../../base'

const leadQueryApi = createDynamicApiClient('CrmLeadQuery')
const leadCommandApi = createCommandApi<CrmLeadCreateDto, CrmLeadUpdateDto, CrmLeadDetailDto>('CrmLead', 'CrmLead')
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
    return leadCommandApiDelete(id)
  },
  convert(id: ApiId) {
    return leadCommandApiAction<CrmLeadDetailDto>('Convert', { id })
  },
  detail(id: ApiId) {
    return leadQueryApi.get<CrmLeadDetailDto | null>('CrmLeadDetail', { id })
  },
  page(input: CrmLeadPageQueryDto) {
    return leadQueryApi.post<PageResult<CrmLeadListItemDto>, CrmLeadPageQueryDto>('CrmLeadPage', input)
  },
  findPotentialDuplicates(input: CrmLeadDuplicateCheckDto) {
    return duplicateQueryApi.post<CrmLeadDuplicateCandidateDto[], CrmLeadDuplicateCheckDto>('FindPotentialDuplicates', input)
  },
}

function leadCommandApiDelete(id: ApiId) {
  return leadCommandRouteApi.delete<void>('CrmLead', { id })
}

function leadCommandApiAction<TResult>(action: string, params: { id: ApiId }) {
  return leadCommandRouteApi.post<TResult, undefined>(action, undefined, { params })
}
