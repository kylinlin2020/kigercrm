import type { ApiId, DateTimeString } from '@/api/types'

export interface WhatsAppChannelBindingInput {
  displayName: string
  whatsAppBusinessAccountId: string
  phoneNumberId: string
  /** Secret-store reference only; never a token or app secret. */
  credentialRef?: string | null
  active: boolean
}

export interface WhatsAppChannelAccountDto {
  basicId: ApiId
  displayName: string
  whatsAppBusinessAccountId: string
  phoneNumberId: string
  credentialRef?: string | null
  status: number
  createdTime: DateTimeString
}

export interface WhatsAppWebhookInboxDto {
  basicId: ApiId
  whatsAppBusinessAccountId: string
  phoneNumberId: string
  eventType: string
  providerMessageId: string
  senderWhatsappId?: string | null
  recipientWhatsappId?: string | null
  providerTimestamp: DateTimeString
  status: string
  attemptCount: number
  processedTime?: DateTimeString | null
  lastError?: string | null
}
