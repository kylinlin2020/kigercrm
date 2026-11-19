<script setup lang="ts">
import type {
  ChatHistoryImportBatch,
  ChatHistoryImportInput,
  ChatHistoryImportResult,
} from '../../../api/chat-history-import.types'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'
import { chatHistoryImportApi } from '../../../api/chat-history-import'

defineOptions({ name: 'ChatHistoryImportPanel' })

const { t } = useI18n()
const { hasPermission } = usePermission()
const canManage = computed(() => hasPermission('message.chat-audit.whatsapp_manage'))

const fileName = ref('')
const sourceChannel = ref('import-preview')
const rowsJson = ref('[{"externalId":"m1","sender":"alice","recipient":"bob","content":"hello","sentAt":"2026-11-18T10:00:00Z"}]')
const result = ref<ChatHistoryImportResult | null>(null)
const batches = ref<ChatHistoryImportBatch[]>([])
const busy = ref(false)

function buildInput(): ChatHistoryImportInput | null {
  try {
    return {
      fileName: fileName.value || 'history.json',
      sourceChannel: sourceChannel.value,
      rows: JSON.parse(rowsJson.value),
    }
  }
  catch {
    toast.danger(t('chat.history.invalidJson'))
    return null
  }
}

async function loadBatches() {
  try {
    batches.value = await chatHistoryImportApi.batches()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.history.loadError'))
  }
}

onMounted(loadBatches)

async function run(importRows: boolean) {
  if (!canManage.value || busy.value) {
    return
  }
  const input = buildInput()
  if (!input) {
    return
  }
  busy.value = true
  try {
    result.value = importRows
      ? await chatHistoryImportApi.import(input)
      : await chatHistoryImportApi.preview(input)
    if (importRows) {
      await loadBatches()
    }
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('chat.history.loadError'))
  }
  finally {
    busy.value = false
  }
}

function verdictLabel(status: number) {
  return [
    t('chat.history.pending'),
    t('chat.history.imported'),
    t('chat.history.duplicate'),
    t('chat.history.invalid'),
  ][status] ?? String(status)
}
</script>

<template>
  <section class="history-import-panel">
    <header class="section-header">
      <div>
        <h3>{{ t('chat.history.title') }}</h3>
        <p>{{ t('chat.history.description') }}</p>
      </div>
    </header>

    <div class="hi-form">
      <label>{{ t('chat.history.fileName') }}
        <input v-model="fileName" type="text" maxlength="260">
      </label>
      <label>{{ t('chat.history.sourceChannel') }}
        <input v-model="sourceChannel" type="text" maxlength="40">
      </label>
      <label>{{ t('chat.history.rowsJson') }}
        <textarea v-model="rowsJson" rows="4" />
      </label>
      <button type="button" :disabled="busy" @click="run(false)">
        {{ t('chat.history.preview') }}
      </button>
      <button v-if="canManage" type="button" :disabled="busy" class="primary-button" @click="run(true)">
        {{ t('chat.history.import') }}
      </button>
    </div>

    <div v-if="result" class="hi-summary">
      {{ t('chat.history.summary', {
        total: result.total,
        imported: result.imported,
        duplicates: result.duplicates,
        invalid: result.invalid,
      }) }}
    </div>
    <div v-for="row in result?.rows ?? []" :key="row.externalId + row.reason" class="hi-row">
      <strong>{{ row.externalId }}</strong>
      <span>{{ verdictLabel(row.status) }} {{ row.reason || '' }}</span>
      <code v-if="row.contentHash">{{ row.contentHash.slice(0, 12) }}</code>
    </div>

    <h4>{{ t('chat.history.batches') }}</h4>
    <p v-if="batches.length === 0" class="hi-note">
      {{ t('chat.history.empty') }}
    </p>
    <div v-for="batch in batches" :key="batch.basicId" class="hi-row">
      <strong>{{ batch.fileName }}</strong>
      <small>{{ batch.sourceChannel }} · {{ t('chat.history.summary', {
        total: batch.totalCount,
        imported: batch.importedCount,
        duplicates: batch.duplicateCount,
        invalid: batch.invalidCount,
      }) }}</small>
    </div>
  </section>
</template>

<style scoped>
.history-import-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.hi-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.hi-form label {
  display: grid;
  flex: 1 1 14rem;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.hi-form input[type='text'],
.hi-form textarea {
  width: 100%;
  font: inherit;
}
.hi-summary {
  font-size: 0.85rem;
  font-weight: 600;
}
.hi-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.35rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.8rem;
}
.hi-row small,
.hi-note {
  color: var(--xh-fg-muted, #777);
}
</style>
