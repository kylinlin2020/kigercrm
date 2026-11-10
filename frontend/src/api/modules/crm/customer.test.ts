import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmCustomerApi } from './customer'

const requestMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('../../request', () => ({
  requestClient: requestMocks,
}))

describe('crmCustomerApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses the shared CRM Customer read routes', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, keyword: 'Acme' } as never

    await crmCustomerApi.page(query)
    await crmCustomerApi.detail('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerQuery/Page', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmCustomerQuery/Detail', expect.objectContaining({
      params: { id: '42' },
    }))
  })

  it('uses the Customer command controller for create, update, and delete', async () => {
    const create = { name: 'Acme' }

    await crmCustomerApi.create(create)
    await crmCustomerApi.update({ basicId: '42', name: 'Acme Updated' } as never)
    await crmCustomerApi.delete('42')

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomer/Create', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmCustomer/Update', expect.objectContaining({
      basicId: '42',
      name: 'Acme Updated',
    }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmCustomer/Delete', expect.objectContaining({
      params: { id: '42' },
    }))
  })
})
