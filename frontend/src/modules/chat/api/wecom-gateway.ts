import type {
  WecomBindingDto,
  WecomBindingInput,
  WecomCommandAuditDto,
} from './wecom-gateway.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('WecomGateway')

/** WeCom tenant bindings and command audit; no outbound send endpoint exists. */
export const wecomGatewayApi = {
  upsertBinding(input: WecomBindingInput) {
    return api.post<WecomBindingDto, WecomBindingInput>('UpsertBinding', input)
  },
  bindings() {
    return api.get<WecomBindingDto[]>('Bindings')
  },
  audit() {
    return api.get<WecomCommandAuditDto[]>('Audit')
  },
}
