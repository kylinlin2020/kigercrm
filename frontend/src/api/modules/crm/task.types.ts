import type { ApiId, BasicDto, BasicUpdateDto, DateTimeString, PageRequest } from '../../types'

export enum CrmTaskStatus {
  Open = 0,
  InProgress = 1,
  Completed = 2,
  Cancelled = 3,
}

export interface CrmTaskPageQueryDto extends PageRequest {
  relatedEntityType?: string | null
  relatedEntityId?: ApiId | null
  status?: CrmTaskStatus | null
  priority?: number | null
  assigneeUserId?: ApiId | null
  dueFrom?: DateTimeString | null
  dueTo?: DateTimeString | null
  overdueOnly?: boolean | null
  keyword?: string | null
}

export interface CrmTaskListItemDto extends BasicDto {
  relatedEntityType?: string | null
  relatedEntityId?: ApiId | null
  title: string
  dueAt?: DateTimeString | null
  status: CrmTaskStatus
  priority: number
  assigneeUserId?: ApiId | null
  completedAt?: DateTimeString | null
}

export interface CrmTaskDetailDto extends CrmTaskListItemDto {
  description?: string | null
}

export interface CrmTaskCreateDto {
  relatedEntityType?: string | null
  relatedEntityId?: ApiId | null
  title: string
  description?: string | null
  dueAt?: DateTimeString | null
  priority: number
  assigneeUserId?: ApiId | null
}

export interface CrmTaskUpdateDto extends BasicUpdateDto {
  relatedEntityType?: string | null
  relatedEntityId?: ApiId | null
  title: string
  description?: string | null
  dueAt?: DateTimeString | null
  status: CrmTaskStatus
  priority: number
  assigneeUserId?: ApiId | null
}

export interface CrmTaskStatusUpdateDto {
  id: ApiId
  status: CrmTaskStatus
}
