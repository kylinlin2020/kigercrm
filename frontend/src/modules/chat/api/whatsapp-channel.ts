import type {
  WhatsAppChannelAccountDto,
  WhatsAppChannelBindingInput,
  WhatsAppWebhookInboxDto,
} from './whatsapp-channel.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('WhatsAppChannel')

/** Tenant-owned WhatsApp bindings and durable webhook inbox review (no outbound send). */
export const whatsappChannelApi = {
  upsertBinding(input: WhatsAppChannelBindingInput) {
    return api.post<WhatsAppChannelAccountDto, WhatsAppChannelBindingInput>('Binding', input)
  },
  bindings() {
    return api.get<WhatsAppChannelAccountDto[]>('Bindings')
  },
  inbox(take: number) {
    return api.get<WhatsAppWebhookInboxDto[]>('Inbox', { take })
  },
}
