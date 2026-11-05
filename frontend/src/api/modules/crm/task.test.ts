import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({
  requestClient: requestMocks,
}))

import { crmTaskApi } from './task'

describe('crmTaskApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses the shared Task read routes', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, overdueOnly: true } as never

    await crmTaskApi.page(query)
    await crmTaskApi.detail('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTaskQuery/CrmTaskPage', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmTaskQuery/CrmTaskDetail', expect.objectContaining({
      params: { id: '42' },
    }))
  })

  it('uses the Task command controller for create, update, delete, and status changes', async () => {
    const create = { title: 'Follow up', priority: 3 }

    await crmTaskApi.create(create)
    await crmTaskApi.update({ ...create, basicId: '42', status: 0 } as never)
    await crmTaskApi.delete('42')
    await crmTaskApi.changeStatus({ id: '42', status: 2 })

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTask/CrmTask', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmTask/CrmTask', expect.objectContaining({
      basicId: '42',
      title: 'Follow up',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmTask/CrmTask', expect.objectContaining({
      params: { id: '42' },
    }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTask/ChangeStatus', { id: '42', status: 2 }, undefined)
  })
})
