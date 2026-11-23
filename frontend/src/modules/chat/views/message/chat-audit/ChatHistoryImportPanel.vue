<script setup lang="ts">
import type {
  ChatHistoryImportBatch,
  ChatHistoryImportInput,
  ChatHistoryImportResult,
} from '../../../api/chat-history-import.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput } from '~/components'
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
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.history.fileName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="fileName" :max-length="260" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('chat.history.sourceChannel') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="sourceChannel" :max-length="40" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="hi-json-field">
        <XhFieldLabel>{{ t('chat.history.rowsJson') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="rowsJson" type="textarea" :rows="4" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="subtle" :disabled="busy" @click="run(false)">
        <XhButtonLabel>{{ t('chat.history.preview') }}</XhButtonLabel>
      </XhButton>
      <XhButton v-if="canManage" variant="outline" :disabled="busy" @click="run(true)">
        <XhButtonLabel>{{ t('chat.history.import') }}</XhButtonLabel>
      </XhButton>
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
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.hi-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.hi-form > :not(button) {
  flex: 1 1 14rem;
  min-width: 0;
}
.hi-json-field {
  flex-basis: 100%;
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
  border-top: 1px solid hsl(var(--border));
  font-size: 0.8rem;
}
.hi-row small,
.hi-note {
  color: hsl(var(--muted-foreground));
}
</style>
