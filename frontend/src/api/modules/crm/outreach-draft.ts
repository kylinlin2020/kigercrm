import type { ApiId } from '../../types'
import type {
  CrmOutreachDraftCreateDto,
  CrmOutreachDraftDto,
  CrmOutreachDraftPreviewDto,
  CrmOutreachDraftReviewDto,
} from './outreach-draft.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmOutreachDraft')

/** Whitelist template interpolation and explicit human review of rendered drafts. */
export const crmOutreachDraftApi = {
  preview(input: CrmOutreachDraftPreviewDto) {
    return api.post<string, CrmOutreachDraftPreviewDto>('Preview', input)
  },
  create(input: CrmOutreachDraftCreateDto) {
    return api.post<CrmOutreachDraftDto, CrmOutreachDraftCreateDto>('Create', input)
  },
  list(enrollmentId: ApiId) {
    return api.get<CrmOutreachDraftDto[]>('List', { enrollmentId })
  },
  getDraft(draftId: ApiId) {
    return api.get<CrmOutreachDraftDto | null>('GetDraft', { draftId })
  },
  approve(draftId: ApiId) {
    return api.post<CrmOutreachDraftDto, undefined>('Approve', undefined, { params: { draftId } })
  },
  reject(input: CrmOutreachDraftReviewDto) {
    return api.post<CrmOutreachDraftDto, CrmOutreachDraftReviewDto>('Reject', input)
  },
}
