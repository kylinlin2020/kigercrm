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

import { crmContactApi } from './contact'

describe('crmContactApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses the shared CRM Contact read routes', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, customerId: 42 } as never

    await crmContactApi.page(query)
    await crmContactApi.detail(17)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmContactQuery/CrmContactPage', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmContactQuery/CrmContactDetail', expect.objectContaining({
      params: { id: 17 },
    }))
  })

  it('uses the Contact command controller for create, update, and delete', async () => {
    const create = { customerId: 42, firstName: 'Alex', lastName: 'Lee', isPrimary: true }

    await crmContactApi.create(create)
    await crmContactApi.update({ basicId: 17, ...create } as never)
    await crmContactApi.delete(17)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmContact/CrmContact', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmContact/CrmContact', expect.objectContaining({
      basicId: 17,
      customerId: 42,
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmContact/CrmContact', expect.objectContaining({
      params: { id: 17 },
    }))
  })
})
