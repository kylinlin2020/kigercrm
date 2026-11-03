import type { ApiId } from '../../types'
import type {
  CrmContactCreateDto,
  CrmContactDetailDto,
  CrmContactListItemDto,
  CrmContactPageQueryDto,
  CrmContactUpdateDto,
} from './contact.types'
import { createCommandApi, createDynamicApiClient, createReadApi } from '../../base'

const contactQueryApi = createReadApi<CrmContactListItemDto, CrmContactDetailDto, CrmContactPageQueryDto>(
  'CrmContactQuery',
  'CrmContact',
)
const contactCommandApi = createCommandApi<CrmContactCreateDto, CrmContactUpdateDto, CrmContactDetailDto>(
  'CrmContact',
  'CrmContact',
)
const contactCommandRouteApi = createDynamicApiClient('CrmContact')

export const crmContactApi = {
  create(input: CrmContactCreateDto) {
    return contactCommandApi.create(input)
  },
  update(input: CrmContactUpdateDto) {
    return contactCommandApi.update(input)
  },
  delete(id: ApiId) {
    return contactCommandRouteApi.delete<void>('CrmContact', { id })
  },
  detail(id: ApiId) {
    return contactQueryApi.detail(id)
  },
  page(input: CrmContactPageQueryDto) {
    return contactQueryApi.page(input)
  },
}
