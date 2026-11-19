import type { ApiId, DateTimeString } from '@/api/types'

export interface BaileysSessionDto {
  basicId: ApiId
  displayName: string
  authStateRef?: string | null
  status: number
  health: number
  qrIssuedAt?: DateTimeString | null
  lastConnectedAt?: DateTimeString | null
  note?: string | null
  createdTime: DateTimeString
}
