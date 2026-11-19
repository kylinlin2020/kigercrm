import type { BaileysSessionDto } from './baileys-session.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('BaileysSession')

/** Operator-confirmed Baileys session contract; no prototype client or QR payload is exposed. */
export const baileysSessionApi = {
  create(displayName: string, authStateRef?: string | null) {
    return api.post<BaileysSessionDto, { displayName: string, authStateRef?: string | null }>('Session', {
      displayName,
      authStateRef,
    })
  },
  transition(sessionId: string, targetStatus: number) {
    return api.post<BaileysSessionDto, { sessionId: string, targetStatus: number }>('Transition', {
      sessionId,
      targetStatus,
    })
  },
  sessions() {
    return api.get<BaileysSessionDto[]>('Sessions')
  },
}
