import type { ApiId } from '../../types'
import type {
  CrmActivityCreateDto,
  CrmActivityDetailDto,
  CrmActivityListItemDto,
  CrmActivityPageQueryDto,
  CrmActivityUpdateDto,
} from './activity.types'
import { createCommandApi, createDynamicApiClient, createReadApi } from '../../base'

const activityReadApi = createReadApi<CrmActivityListItemDto, CrmActivityDetailDto, CrmActivityPageQueryDto>(
  'CrmActivityQuery',
  'CrmActivity',
)
const activityCommandApi = createCommandApi<CrmActivityCreateDto, CrmActivityUpdateDto, CrmActivityDetailDto>(
  'CrmActivity',
  'CrmActivity',
)
const activityCommandRouteApi = createDynamicApiClient('CrmActivity')

export const crmActivityApi = {
  create(input: CrmActivityCreateDto) {
    return activityCommandApi.create(input)
  },
  update(input: CrmActivityUpdateDto) {
    return activityCommandApi.update(input)
  },
  delete(id: ApiId) {
    return activityCommandRouteApi.delete<void>('CrmActivity', { id })
  },
  detail(id: ApiId) {
    return activityReadApi.detail(id)
  },
  page(input: CrmActivityPageQueryDto) {
    return activityReadApi.page(input)
  },
}
