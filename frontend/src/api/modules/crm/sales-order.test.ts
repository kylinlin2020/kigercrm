import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmSalesOrderApi } from './sales-order'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

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
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrderQuery/Page', page, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmSalesOrderQuery/Detail', expect.objectContaining({ params: { id: '9' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrderQuery/LinesPage', lines, undefined)
  })

  it('uses the direct order creation command route', async () => {
    const input = { orderNumber: '', numberingIdempotencyKey: 'order-create-key', customerId: '5', currency: 'USD', lines: [{ description: 'Generator', quantity: 2, unitPrice: 100 }] } as never
    await crmSalesOrderApi.create(input)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrder/Create', input, undefined)
  })

  it('uses the draft-only update command route', async () => {
    const input = { id: '9', customerId: '5', currency: 'USD', lines: [{ id: '12', description: 'Generator', quantity: 2, unitPrice: 100 }] } as never
    await crmSalesOrderApi.updateDraft(input)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmSalesOrder/Draft', input, undefined)
  })

  it('passes new draft line ID zero through the update command unchanged', async () => {
    const input = { id: '9', customerId: '5', currency: 'USD', lines: [{ id: '0', description: 'Replacement pump', quantity: 1, unitPrice: 250 }] } as never
    await crmSalesOrderApi.updateDraft(input)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmSalesOrder/Draft', input, undefined)
  })

  it('uses the order lifecycle command route', async () => {
    const input = { id: '9', status: 1 } as never
    await crmSalesOrderApi.changeStatus(input)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmSalesOrder/ChangeStatus', input, undefined)
  })
})
