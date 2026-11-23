<script setup lang="ts">
import type {
  WecomBindingDto,
  WecomCommandAuditDto,
} from '../../../api/wecom-gateway.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput } from '~/components'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'
import { wecomGatewayApi } from '../../../api/wecom-gateway'

defineOptions({ name: 'WecomGatewayPanel' })

const { t } = useI18n()
const { hasPermission } = usePermission()
const canManage = computed(() => hasPermission('message.chat-audit.whatsapp_manage'))

const displayName = ref('')
const corpId = ref('')
const agentId = ref('')
const credentialRef = ref('')
const bindings = ref<WecomBindingDto[]>([])
const audit = ref<WecomCommandAuditDto[]>([])

async function load() {
  try {
    const [rows, events] = await Promise.all([wecomGatewayApi.bindings(), wecomGatewayApi.audit()])
    bindings.value = rows
    audit.value = events
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.wecom.loadError'))
  }
}

onMounted(load)

async function saveBinding() {
  if (!canManage.value || !corpId.value || !agentId.value) {
    return
  }
  try {
    await wecomGatewayApi.upsertBinding({
      displayName: displayName.value || corpId.value,
      corpId: corpId.value,
      agentId: agentId.value,
      credentialRef: credentialRef.value || null,
      active: true,
    })
    toast.success(t('chat.wecom.saved'))
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.wecom.loadError'))
  }
}
</script>

<template>
  <section class="wecom-panel">
    <header class="section-header">
      <div>
        <h3>{{ t('chat.wecom.title') }}</h3>
        <p>{{ t('chat.wecom.description') }}</p>
      </div>
    </header>
    <div class="wc-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.wecom.displayName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="displayName" :max-length="160" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.wecom.corpId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="corpId" :max-length="120" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.wecom.agentId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="agentId" :max-length="120" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.wecom.credentialRef') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="credentialRef" :max-length="300" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton v-if="canManage" variant="outline" :disabled="!corpId || !agentId" @click="saveBinding">
        <XhButtonLabel>{{ t('chat.wecom.save') }}</XhButtonLabel>
      </XhButton>
    </div>
    <div v-for="binding in bindings" :key="binding.basicId" class="wc-row">
      <strong>{{ binding.displayName }}</strong>
      <small>{{ binding.corpId }} / {{ binding.agentId }}</small>
    </div>
    <h4>{{ t('chat.wecom.auditTitle') }}</h4>
    <div v-if="audit.length === 0" class="wc-note">
      {{ t('chat.wecom.auditEmpty') }}
    </div>
    <div v-for="item in audit" :key="item.basicId" class="wc-row">
      <strong>{{ item.command }}</strong>
      <small>{{ item.fromUserId }} · {{ item.reason }}</small>
    </div>
  </section>
</template>

<style scoped>
.wecom-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.wc-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.wc-form > :not(button) {
  flex: 1 1 12rem;
  min-width: 0;
}
.wc-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.35rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.8rem;
}
.wc-note,
.wc-row small {
  color: hsl(var(--muted-foreground));
}
</style>
