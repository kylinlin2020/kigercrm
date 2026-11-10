import type { ApiId } from '../../types'
import type {
  CrmContactCreateDto,
  CrmContactDetailDto,
  CrmContactListItemDto,
  CrmContactPageQueryDto,
  CrmContactUpdateDto,
} from './contact.types'
import { createCrmCommandClient, createCrmReadClient } from './crud-client'

const contactQueryApi = createCrmReadClient<CrmContactListItemDto, CrmContactDetailDto, CrmContactPageQueryDto>('CrmContactQuery')
const contactCommandApi = createCrmCommandClient<CrmContactCreateDto, CrmContactUpdateDto, CrmContactDetailDto>('CrmContact')

export const crmContactApi = {
  create(input: CrmContactCreateDto) {
    return contactCommandApi.create(input)
  },
  update(input: CrmContactUpdateDto) {
    return contactCommandApi.update(input)
  },
  delete(id: ApiId) {
    return contactCommandApi.delete(id)
  },
  detail(id: ApiId) {
    return contactQueryApi.detail(id)
  },
  page(input: CrmContactPageQueryDto) {
    return contactQueryApi.page(input)
  },
}
