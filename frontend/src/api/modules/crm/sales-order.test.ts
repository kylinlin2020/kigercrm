import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

import { crmSalesOrderApi } from './sales-order'

describe('crmSalesOrderApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
  })

  it('uses order query routes for page, detail and fulfillment lines', async () => {
    const page = { page: { pageIndex: 1, pageSize: 20 }, status: 3 } as never
    const lines = { page: { pageIndex: 1, pageSize: 20 }, salesOrderId: '9' } as never
    await crmSalesOrderApi.page(page)
    await crmSalesOrderApi.detail('9')
    await crmSalesOrderApi.lines(lines)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrderQuery/CrmSalesOrderPage', page, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmSalesOrderQuery/CrmSalesOrderDetail', expect.objectContaining({ params: { id: '9' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrderQuery/CrmSalesOrderLinesPage', lines, undefined)
  })

  it('uses the order lifecycle command route', async () => {
    const input = { id: '9', status: 1 } as never
    await crmSalesOrderApi.changeStatus(input)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrder/ChangeStatus', input, undefined)
  })
})
