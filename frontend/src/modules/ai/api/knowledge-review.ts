import type {
  KnowledgeReviewDecisionDto,
  KnowledgeReviewDto,
} from './knowledge-review.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('KnowledgeReview')

/** Knowledge document review workflow, decoupled from indexing status. */
export const knowledgeReviewApi = {
  submit(documentId: string) {
    return api.post<KnowledgeReviewDto, undefined>('Submit', undefined, { params: { documentId } })
  },
  decide(input: KnowledgeReviewDecisionDto) {
    return api.post<KnowledgeReviewDto, KnowledgeReviewDecisionDto>('Decide', input)
  },
  reviews(documentId: string) {
    return api.get<KnowledgeReviewDto[]>('Reviews', { documentId })
  },
}
