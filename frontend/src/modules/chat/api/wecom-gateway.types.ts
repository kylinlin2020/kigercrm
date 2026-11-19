import type { ApiId, DateTimeString } from '@/api/types'

export interface WecomBindingInput {
  displayName: string
  corpId: string
  agentId: string
  credentialRef?: string | null
  active: boolean
}

export interface WecomBindingDto {
  basicId: ApiId
  displayName: string
  corpId: string
  agentId: string
  credentialRef?: string | null
  status: number
  createdTime: DateTimeString
}

export interface WecomCommandAuditDto {
  basicId: ApiId
  corpId: string
  agentId: string
  fromUserId: string
  providerMessageId: string
  command: string
  outcome: number
  reason?: string | null
  replyHash: string
}
