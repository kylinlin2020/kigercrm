import { beforeEach, describe, expect, it, vi } from 'vitest'

import { knowledgeTagsApi } from './knowledge-tags'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/request', () => ({ requestClient: requestMocks }))

describe('knowledgeTagsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue([])
  })

  it('routes tag management and duplicate reporting through the tag service', async () => {
    await knowledgeTagsApi.setTags({ documentId: '5', tags: ['Product', 'Sales'] })
    await knowledgeTagsApi.tags()
    await knowledgeTagsApi.duplicates()

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/KnowledgeTag/SetTags',
      { documentId: '5', tags: ['Product', 'Sales'] },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/KnowledgeTag/Tags', {})
    expect(requestMocks.get).toHaveBeenCalledWith('/KnowledgeTag/Duplicates', {})
  })
})
