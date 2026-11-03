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

import { crmLeadApi } from './lead'

describe('crmLeadApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue(null)
    requestMocks.post.mockResolvedValue([])
    requestMocks.put.mockResolvedValue({})
    requestMocks.delete.mockResolvedValue(undefined)
  })

  it('uses backend query service action names for lead page and detail', async () => {
    const query = { page: { pageIndex: 1, pageSize: 20 }, keyword: 'Acme' } as never

    await crmLeadApi.page(query)
    await crmLeadApi.detail(42)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmLeadQuery/CrmLeadPage', query, undefined)
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmLeadQuery/CrmLeadDetail', expect.objectContaining({
      params: { id: 42 },
    }))
  })

  it('uses the command service and duplicate query controller routes', async () => {
    const create = { companyName: 'Acme' }
    const duplicate = { email: 'sales@acme.example' }

    await crmLeadApi.create(create)
    await crmLeadApi.update({ basicId: 42, companyName: 'Acme Updated', status: 0 } as never)
    await crmLeadApi.delete(42)
    await crmLeadApi.convert(42)
    await crmLeadApi.findPotentialDuplicates(duplicate)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmLead/CrmLead', create, undefined)
    expect(requestMocks.put).toHaveBeenCalledWith('/CrmLead/CrmLead', expect.objectContaining({ basicId: 42 }), undefined)
    expect(requestMocks.delete).toHaveBeenCalledWith('/CrmLead/CrmLead', expect.objectContaining({ params: { id: 42 } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmLead/Convert', null, expect.objectContaining({ params: { id: 42 } }))
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmLeadDuplicateQuery/FindPotentialDuplicates', duplicate, undefined)
  })
})
