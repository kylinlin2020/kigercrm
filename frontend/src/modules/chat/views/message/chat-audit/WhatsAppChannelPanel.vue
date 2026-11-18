<script setup lang="ts">
import type {
  WhatsAppChannelAccountDto,
  WhatsAppWebhookInboxDto,
} from '../../../api/whatsapp-channel.types'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'
import { whatsappChannelApi } from '../../../api/whatsapp-channel'

defineOptions({ name: 'WhatsAppChannelPanel' })

const { t } = useI18n()

const displayName = ref('')
const wabaId = ref('')
const phoneId = ref('')
const credentialRef = ref('')
const active = ref(true)
const bindings = ref<WhatsAppChannelAccountDto[]>([])
const inbox = ref<WhatsAppWebhookInboxDto[]>([])
const loading = ref(false)

const { hasPermission } = usePermission()
const canManage = computed(() => hasPermission('message.chat-audit.whatsapp_manage'))

async function load() {
  loading.value = true
  try {
    const [rows, events] = await Promise.all([
      whatsappChannelApi.bindings(),
      whatsappChannelApi.inbox(50),
    ])
    bindings.value = rows
    inbox.value = events
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.whatsapp.loadError'))
  }
  finally {
    loading.value = false
  }
}

onMounted(load)

async function saveBinding() {
  if (!canManage.value || !wabaId.value || !phoneId.value) {
    return
  }
  try {
    await whatsappChannelApi.upsertBinding({
      displayName: displayName.value || wabaId.value,
      whatsAppBusinessAccountId: wabaId.value,
      phoneNumberId: phoneId.value,
      credentialRef: credentialRef.value || null,
      active: active.value,
    })
    toast.success(t('chat.whatsapp.bindingSaved'))
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.whatsapp.loadError'))
  }
}
</script>

<template>
  <section class="whatsapp-panel">
    <header class="section-header">
      <div>
        <h3>{{ t('chat.whatsapp.title') }}</h3>
        <p>{{ t('chat.whatsapp.description') }}</p>
      </div>
    </header>

    <div class="wa-form">
      <label>{{ t('chat.whatsapp.displayName') }}
        <input v-model="displayName" type="text" maxlength="160">
      </label>
      <label>{{ t('chat.whatsapp.wabaId') }}
        <input v-model="wabaId" type="text" maxlength="100">
      </label>
      <label>{{ t('chat.whatsapp.phoneId') }}
        <input v-model="phoneId" type="text" maxlength="100">
      </label>
      <label>{{ t('chat.whatsapp.credentialRef') }}
        <input v-model="credentialRef" type="text" maxlength="300">
      </label>
      <label class="wa-check">
        <input v-model="active" type="checkbox">
        {{ t('chat.whatsapp.active') }}
      </label>
      <button v-if="canManage" type="button" :disabled="loading || !wabaId || !phoneId" @click="saveBinding">
        {{ t('chat.whatsapp.saveBinding') }}
      </button>
    </div>

    <div v-for="binding in bindings" :key="binding.basicId" class="wa-row">
      <strong>{{ binding.displayName }}</strong>
      <small>{{ binding.whatsAppBusinessAccountId }} / {{ binding.phoneNumberId }}</small>
      <span class="wa-state">{{ binding.status === 0 ? t('chat.whatsapp.active') : t('chat.whatsapp.disabled') }}</span>
    </div>

    <h4>{{ t('chat.whatsapp.inboxTitle') }}</h4>
    <div v-if="inbox.length === 0" class="wa-empty">
      {{ t('chat.whatsapp.inboxEmpty') }}
    </div>
    <table v-else class="wa-table">
      <thead>
        <tr>
          <th>{{ t('chat.whatsapp.eventType') }}</th>
          <th>{{ t('chat.whatsapp.providerMessageId') }}</th>
          <th>{{ t('chat.whatsapp.status') }}</th>
          <th>{{ t('chat.whatsapp.attempts') }}</th>
          <th>{{ t('chat.whatsapp.lastError') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="event in inbox" :key="event.basicId">
          <td>{{ event.eventType }}</td>
          <td>{{ event.providerMessageId }}</td>
          <td>{{ event.status }}</td>
          <td>{{ event.attemptCount }}</td>
          <td>{{ event.lastError || '—' }}</td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.whatsapp-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.wa-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.wa-form label {
  display: grid;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.wa-form input[type='text'] {
  width: 100%;
  font: inherit;
}
.wa-check {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.wa-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.wa-row small,
.wa-empty {
  color: var(--xh-fg-muted, #777);
}
.wa-state {
  margin-left: auto;
}
.wa-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}
.wa-table th,
.wa-table td {
  padding: 0.35rem 0.4rem;
  border-bottom: 1px solid var(--xh-border-subtle, #ddd);
  text-align: left;
}
</style>
