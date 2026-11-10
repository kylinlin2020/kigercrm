import type { ApiId } from '../../types'
import type {
  CrmTaskCreateDto,
  CrmTaskDetailDto,
  CrmTaskListItemDto,
  CrmTaskPageQueryDto,
  CrmTaskStatusUpdateDto,
  CrmTaskUpdateDto,
} from './task.types'
import { createDynamicApiClient } from '../../base'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const taskReadApi = createCrmReadClient<CrmTaskListItemDto, CrmTaskDetailDto, CrmTaskPageQueryDto>('CrmTaskQuery')
const taskCommandApi = createCrmCommandClient<CrmTaskCreateDto, CrmTaskUpdateDto, CrmTaskDetailDto>('CrmTask')
const taskCommandRouteApi = createDynamicApiClient('CrmTask')

export const crmTaskApi = {
  create(input: CrmTaskCreateDto) {
    return taskCommandApi.create(input)
  },
  update(input: CrmTaskUpdateDto) {
    return taskCommandApi.update(input)
  },
  delete(id: ApiId) {
    return taskCommandApi.delete(id)
  },
  detail(id: ApiId) {
    return taskReadApi.detail(id)
  },
  page(input: CrmTaskPageQueryDto) {
    return taskReadApi.page(input)
  },
  changeStatus(input: CrmTaskStatusUpdateDto) {
    return taskCommandRouteApi.post<CrmTaskDetailDto, CrmTaskStatusUpdateDto>('ChangeStatus', input)
  },
}
