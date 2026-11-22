import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmCustomerPoolApi } from './customer-pool'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmCustomerPoolApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes pool management through the customer pool service', async () => {
    await crmCustomerPoolApi.add({ customerId: '7' })
    await crmCustomerPoolApi.available()
    await crmCustomerPoolApi.mine()
    await crmCustomerPoolApi.claim({ entryId: '9' })
    await crmCustomerPoolApi.release({ entryId: '9' })
    await crmCustomerPoolApi.assign('9', '42')
    await crmCustomerPoolApi.recoverIdle()

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerPool/Add', { customerId: '7' }, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmCustomerPool/Available', {})
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmCustomerPool/Mine', {})
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerPool/Claim', { entryId: '9' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerPool/Release', { entryId: '9' }, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerPool/Assign', undefined, {
      params: { entryId: '9', userId: '42' },
    })
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerPool/RecoverIdle', undefined, undefined)
  })
})
