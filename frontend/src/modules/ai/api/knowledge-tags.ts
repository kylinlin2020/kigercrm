import type {
  KnowledgeDuplicateReportDto,
  KnowledgeTagItemDto,
  KnowledgeTagsUpdateDto,
} from './knowledge-tags.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('KnowledgeTag')

/** Knowledge document tags and byte-identical duplicate detection. */
export const knowledgeTagsApi = {
  setTags(input: KnowledgeTagsUpdateDto) {
    return api.post<KnowledgeTagItemDto[], KnowledgeTagsUpdateDto>('SetTags', input)
  },
  tags() {
    return api.get<KnowledgeTagItemDto[]>('Tags')
  },
  duplicates() {
    return api.get<KnowledgeDuplicateReportDto>('Duplicates')
  },
}
