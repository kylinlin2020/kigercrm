import { beforeEach, describe, expect, it, vi } from 'vitest'

import { knowledgeReviewApi } from './knowledge-review'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/request', () => ({ requestClient: requestMocks }))

describe('knowledgeReviewApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes review submission, decision and history through the review service', async () => {
    await knowledgeReviewApi.submit('100')
    await knowledgeReviewApi.decide({ reviewId: '200', approve: true, comment: 'ok' })
    await knowledgeReviewApi.reviews('100')

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/KnowledgeReview/Submit',
      undefined,
      expect.objectContaining({ params: { documentId: '100' } }),
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/KnowledgeReview/Decide',
      { reviewId: '200', approve: true, comment: 'ok' },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/KnowledgeReview/Reviews', { params: { documentId: '100' } })
  })
})
