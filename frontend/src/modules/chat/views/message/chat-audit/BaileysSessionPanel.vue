<script setup lang="ts">
import type { BaileysSessionDto } from '../../../api/baileys-session.types'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
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
      <label>{{ t('chat.baileys.displayName') }}
        <input v-model="displayName" type="text" maxlength="160">
      </label>
      <label>{{ t('chat.baileys.authStateRef') }}
        <input v-model="authStateRef" type="text" maxlength="300">
      </label>
      <button v-if="canManage" type="button" :disabled="!displayName" @click="create">
        {{ t('chat.baileys.create') }}
      </button>
    </div>
    <div v-for="session in sessions" :key="session.basicId" class="bl-row">
      <div class="bl-main">
        <strong>{{ session.displayName }}</strong>
        <small>{{ statusLabel(session.status) }} · {{ session.note || '—' }}</small>
      </div>
      <button
        v-if="canManage && session.status === 0"
        type="button"
        @click="transition(session.basicId, 1)"
      >
        {{ t('chat.baileys.issueQr') }}
      </button>
      <button
        v-if="canManage && session.status === 1"
        type="button"
        @click="transition(session.basicId, 2)"
      >
        {{ t('chat.baileys.confirmConnected') }}
      </button>
      <button
        v-if="canManage && session.status !== 3"
        type="button"
        @click="transition(session.basicId, 3)"
      >
        {{ t('chat.baileys.revoke') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.baileys-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.bl-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.bl-form label {
  display: grid;
  flex: 1 1 12rem;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.bl-form input {
  width: 100%;
  font: inherit;
}
.bl-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.bl-main {
  display: grid;
  gap: 0.2rem;
}
.bl-main small {
  color: var(--xh-fg-muted, #777);
}
</style>
