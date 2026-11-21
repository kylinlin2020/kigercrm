import { beforeEach, describe, expect, it, vi } from 'vitest'

import { knowledgeAccessApi } from './knowledge-access'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn() }))
vi.mock('@/api/request', () => ({ requestClient: requestMocks }))

describe('knowledgeAccessApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue({})
    requestMocks.post.mockResolvedValue({})
  })

  it('routes ACL get and update through the knowledge access service', async () => {
    await knowledgeAccessApi.get('7')
    await knowledgeAccessApi.update({ documentId: '7', visibility: 1, allowedRoles: ['sales'] })

    expect(requestMocks.get).toHaveBeenCalledWith('/KnowledgeAccess/Get', { params: { documentId: '7' } })
    expect(requestMocks.put).toHaveBeenCalledWith(
      '/KnowledgeAccess/Update',
      { documentId: '7', visibility: 1, allowedRoles: ['sales'] },
      undefined,
    )
  })
})
