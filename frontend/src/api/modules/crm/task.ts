import type { ApiId } from '../../types'
import type {
  CrmTaskCreateDto,
  CrmTaskDetailDto,
  CrmTaskListItemDto,
  CrmTaskPageQueryDto,
  CrmTaskStatusUpdateDto,
  CrmTaskUpdateDto,
} from './task.types'
import { createCommandApi, createDynamicApiClient, createReadApi } from '../../base'

const taskReadApi = createReadApi<CrmTaskListItemDto, CrmTaskDetailDto, CrmTaskPageQueryDto>(
  'CrmTaskQuery',
  'CrmTask',
)
const taskCommandApi = createCommandApi<CrmTaskCreateDto, CrmTaskUpdateDto, CrmTaskDetailDto>('CrmTask', 'CrmTask')
const taskCommandRouteApi = createDynamicApiClient('CrmTask')

export const crmTaskApi = {
  create(input: CrmTaskCreateDto) {
    return taskCommandApi.create(input)
  },
  update(input: CrmTaskUpdateDto) {
    return taskCommandApi.update(input)
  },
  delete(id: ApiId) {
    return taskCommandRouteApi.delete<void>('CrmTask', { id })
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
