<script setup lang="ts">
import type { BaileysSessionDto } from '../../../api/baileys-session.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput } from '~/components'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'
import { baileysSessionApi } from '../../../api/baileys-session'

defineOptions({ name: 'BaileysSessionPanel' })

const { t } = useI18n()
const { hasPermission } = usePermission()
const canManage = computed(() => hasPermission('message.chat-audit.whatsapp_manage'))

const displayName = ref('')
const authStateRef = ref('')
const sessions = ref<BaileysSessionDto[]>([])

async function load() {
  try {
    sessions.value = await baileysSessionApi.sessions()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.baileys.loadError'))
  }
}

onMounted(load)

async function create() {
  if (!canManage.value || !displayName.value) {
    return
  }
  try {
    await baileysSessionApi.create(displayName.value, authStateRef.value || null)
    toast.success(t('chat.baileys.created'))
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.baileys.loadError'))
  }
}

async function transition(sessionId: string, targetStatus: number) {
  if (!canManage.value) {
    return
  }
  try {
    await baileysSessionApi.transition(sessionId, targetStatus)
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.baileys.loadError'))
  }
}

function statusLabel(status: number) {
  return [
    t('chat.baileys.disconnected'),
    t('chat.baileys.qrPending'),
    t('chat.baileys.connected'),
    t('chat.baileys.revoked'),
  ][status] ?? String(status)
}
</script>

<template>
  <section class="baileys-panel">
    <header class="section-header">
      <div>
        <h3>{{ t('chat.baileys.title') }}</h3>
        <p>{{ t('chat.baileys.description') }}</p>
      </div>
    </header>
    <div class="bl-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.baileys.displayName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="displayName" :max-length="160" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.baileys.authStateRef') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="authStateRef" :max-length="300" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton v-if="canManage" variant="outline" :disabled="!displayName" @click="create">
        <XhButtonLabel>{{ t('chat.baileys.create') }}</XhButtonLabel>
      </XhButton>
    </div>
    <div v-for="session in sessions" :key="session.basicId" class="bl-row">
      <div class="bl-main">
        <strong>{{ session.displayName }}</strong>
        <small>{{ statusLabel(session.status) }} · {{ session.note || '—' }}</small>
      </div>
      <XhButton
        v-if="canManage && session.status === 0"
        variant="outline"
        @click="transition(session.basicId, 1)"
      >
        <XhButtonLabel>{{ t('chat.baileys.issueQr') }}</XhButtonLabel>
      </XhButton>
      <XhButton
        v-if="canManage && session.status === 1"
        variant="outline"
        @click="transition(session.basicId, 2)"
      >
        <XhButtonLabel>{{ t('chat.baileys.confirmConnected') }}</XhButtonLabel>
      </XhButton>
      <XhButton
        v-if="canManage && session.status !== 3"
        variant="subtle"
        tone="danger"
        @click="transition(session.basicId, 3)"
      >
        <XhButtonLabel>{{ t('chat.baileys.revoke') }}</XhButtonLabel>
      </XhButton>
    </div>
  </section>
</template>

<style scoped>
.baileys-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.bl-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.bl-form > :not(button) {
  flex: 1 1 12rem;
  min-width: 0;
}
.bl-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.bl-main {
  display: grid;
  gap: 0.2rem;
}
.bl-main small {
  color: hsl(var(--muted-foreground));
}
</style>
