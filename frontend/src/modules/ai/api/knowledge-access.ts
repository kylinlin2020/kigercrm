import type {
  KnowledgeAccessDto,
  KnowledgeAccessUpdateDto,
} from './knowledge-access.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('KnowledgeAccess')

/** Knowledge document ACL management: visibility and role allowlist. */
export const knowledgeAccessApi = {
  get(documentId: string) {
    return api.get<KnowledgeAccessDto>('Get', { documentId })
  },
  update(input: KnowledgeAccessUpdateDto) {
    return api.put<KnowledgeAccessDto, KnowledgeAccessUpdateDto>('Update', input)
  },
}
