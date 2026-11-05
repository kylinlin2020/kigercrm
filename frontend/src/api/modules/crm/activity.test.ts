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

import { crmActivityApi } from './activity'

describe('crmActivityApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses the shared CRM Activity read routes', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, relatedEntityType: 'customer' } as never

    await crmActivityApi.page(query)
    await crmActivityApi.detail(11)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmActivityQuery/CrmActivityPage', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmActivityQuery/CrmActivityDetail', expect.objectContaining({
      params: { id: 11 },
    }))
  })

  it('uses the Activity command controller for create, update, and delete', async () => {
    const create = {
      relatedEntityType: 'customer',
      relatedEntityId: 42,
      activityType: 0,
      subject: 'Discovery call',
      occurredAt: '2026-11-03T10:00:00.000Z',
    }

    await crmActivityApi.create(create)
    await crmActivityApi.update({ basicId: 11, ...create } as never)
    await crmActivityApi.delete(11)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmActivity/CrmActivity', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmActivity/CrmActivity', expect.objectContaining({
      basicId: 11,
      subject: 'Discovery call',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmActivity/CrmActivity', expect.objectContaining({
      params: { id: 11 },
    }))
  })
})
