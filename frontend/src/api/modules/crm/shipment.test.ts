import { beforeEach, describe, expect, it, vi } from 'vitest'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

import { crmShipmentApi } from './shipment'

describe('crmShipmentApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue({ items: [], page: { totalCount: 0 } })
  })

  it('uses shipment query routes for page, detail and lines', async () => {
    const page = { page: { pageIndex: 1, pageSize: 20 }, status: 0 } as never
    const lines = { page: { pageIndex: 1, pageSize: 20 }, shipmentId: '9' } as never
    await crmShipmentApi.page(page)
    await crmShipmentApi.detail('9')
    await crmShipmentApi.lines(lines)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmShipmentQuery/CrmShipmentPage', page, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmShipmentQuery/CrmShipmentDetail', expect.objectContaining({ params: { id: '9' } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmShipmentQuery/CrmShipmentLinesPage', lines, undefined)
  })

  it('uses shipment creation and lifecycle command routes', async () => {
    const input = { shipmentNumber: 'S-1', salesOrderId: '8', lines: [{ salesOrderLineId: '10', quantity: 2 }, { salesOrderLineId: '11', quantity: 1.5 }] } as never
    const status = { id: '9', status: 1 } as never
    await crmShipmentApi.create(input)
    await crmShipmentApi.changeStatus(status)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmShipment/CrmShipment', input, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmShipment/ChangeStatus', status, undefined)
  })
})
