import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmTaskApi } from './task'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({
  requestClient: requestMocks,
}))

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

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTaskQuery/Page', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmTaskQuery/Detail', expect.objectContaining({
      params: { id: '42' },
    }))
  })

  it('uses the Task command controller for create, update, delete, and status changes', async () => {
    const create = { title: 'Follow up', priority: 3 }

    await crmTaskApi.create(create)
    await crmTaskApi.update({ ...create, basicId: '42', status: 0 } as never)
    await crmTaskApi.delete('42')
    await crmTaskApi.changeStatus({ id: '42', status: 2 })

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTask/Create', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmTask/Update', expect.objectContaining({
      basicId: '42',
      title: 'Follow up',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmTask/Delete', expect.objectContaining({
      params: { id: '42' },
    }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmTask/ChangeStatus', { id: '42', status: 2 }, undefined)
  })
})
