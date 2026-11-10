import type { ApiId, PageRequest, PageResult } from '../../types'
import { createDynamicApiClient } from '../../base'

/**
 * CRM 服务的 DynamicApi 动作名与平台模块不同，不要复用 createReadApi / createCommandApi。
 *
 * 平台服务方法带资源名（GetAccessLogPageAsync / CreateOperationAsync），框架剥掉
 * Get/Create/Update/Delete 前缀后剩下 AccessLogPage / Operation，因此通用 helper 会把
 * 资源名拼进动作段。CRM 服务方法用的是通用短名（GetPageAsync / CreateAsync / ...），
 * 剥掉前缀后只剩 Page / Create；方法名本身就是动词时（CreateAsync）则保留动词。
 * 因此 CRM 的真实路由是 /CrmXQuery/Page、/CrmX/Create 这类不带资源前缀的地址。
 *
 * 改动后端方法名会同时改变这些地址；两端必须一起核对，别再按平台模板推 URL。
 */

export interface CrmReadClient<TListItem, TDetail, TQuery extends PageRequest> {
  detail: (id: ApiId) => Promise<TDetail | null>
  page: (input: TQuery) => Promise<PageResult<TListItem>>
}

export interface CrmCommandClient<TCreate, TUpdate, TDetail> {
  create: (input: TCreate) => Promise<TDetail>
  delete: (id: ApiId) => Promise<void>
  update: (input: TUpdate) => Promise<TDetail>
}

export function createCrmReadClient<TListItem, TDetail, TQuery extends PageRequest>(
  queryController: string,
): CrmReadClient<TListItem, TDetail, TQuery> {
  const api = createDynamicApiClient(queryController)

  return {
    page(input: TQuery) {
      return api.post<PageResult<TListItem>, TQuery>('Page', input)
    },
    detail(id: ApiId) {
      return api.get<TDetail | null>('Detail', { id })
    },
  }
}

export function createCrmCommandClient<TCreate, TUpdate, TDetail>(
  controller: string,
): CrmCommandClient<TCreate, TUpdate, TDetail> {
  const api = createDynamicApiClient(controller)

  return {
    create(input: TCreate) {
      return api.post<TDetail, TCreate>('Create', input)
    },
    update(input: TUpdate) {
      return api.put<TDetail, TUpdate>('Update', input)
    },
    delete(id: ApiId) {
      return api.delete<void>('Delete', { id })
    },
  }
}
