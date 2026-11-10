import type { ApiId } from '../../types'
import type {
  CrmActivityCreateDto,
  CrmActivityDetailDto,
  CrmActivityListItemDto,
  CrmActivityPageQueryDto,
  CrmActivityUpdateDto,
} from './activity.types'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const activityReadApi = createCrmReadClient<CrmActivityListItemDto, CrmActivityDetailDto, CrmActivityPageQueryDto>('CrmActivityQuery')
const activityCommandApi = createCrmCommandClient<CrmActivityCreateDto, CrmActivityUpdateDto, CrmActivityDetailDto>('CrmActivity')

export const crmActivityApi = {
  create(input: CrmActivityCreateDto) {
    return activityCommandApi.create(input)
  },
  update(input: CrmActivityUpdateDto) {
    return activityCommandApi.update(input)
  },
  delete(id: ApiId) {
    return activityCommandApi.delete(id)
  },
  detail(id: ApiId) {
    return activityReadApi.detail(id)
  },
  page(input: CrmActivityPageQueryDto) {
    return activityReadApi.page(input)
  },
}
