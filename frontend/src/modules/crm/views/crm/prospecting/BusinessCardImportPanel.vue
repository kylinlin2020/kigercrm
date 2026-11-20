<script setup lang="ts">
import type {
  CrmBusinessCardPreviewDto,
  CrmBusinessCardRowDto,
} from '@/api/modules/crm/business-card-import.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmBusinessCardImportApi } from '@/api/modules/crm/business-card-import'
import { toast } from '~/composables'

defineOptions({ name: 'CrmBusinessCardImportPanel' })

const { t } = useI18n()
const rowsJson = ref('')
const preview = ref<CrmBusinessCardPreviewDto | null>(null)
const batches = ref<string[]>([])
const batchId = ref('')
const rows = ref<CrmBusinessCardRowDto[]>([])
const rejectReason = ref('')

const example = '[{"externalId":"1","contactName":"Alice","companyName":"Acme","email":"alice@acme.com","phone":"13800138000"}]'

function statusLabel(status: number) {
  return [
    t('crm.prospecting.cardPending'),
    t('crm.prospecting.cardConfirmed'),
    t('crm.prospecting.cardRejected'),
  ][status] ?? String(status)
}

async function reloadBatches() {
  try {
    batches.value = await crmBusinessCardImportApi.batches()
    if (!batches.value.includes(batchId.value))
      batchId.value = batches.value[0] ?? ''
    await reloadRows()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.cardError'))
  }
}

async function reloadRows() {
  if (!batchId.value) {
    rows.value = []
    return
  }
  try {
    rows.value = await crmBusinessCardImportApi.rows(batchId.value)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.cardError'))
  }
}

async function runPreview() {
  try {
    preview.value = await crmBusinessCardImportApi.preview({ rowsJson: rowsJson.value })
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.cardError'))
  }
}

async function runImport() {
  try {
    const result = await crmBusinessCardImportApi.import({ rowsJson: rowsJson.value })
    toast.success(t('crm.prospecting.cardImported', { count: result.staged }))
    batchId.value = result.batchId
    await reloadBatches()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.cardError'))
  }
}

async function decide(rowId: string, approve: boolean) {
  try {
    if (approve)
      await crmBusinessCardImportApi.confirm({ rowId })
    else await crmBusinessCardImportApi.reject({ rowId, reason: rejectReason.value || 'manual reject' })
    await reloadRows()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.cardError'))
  }
}
</script>

<template>
  <section class="card-import">
    <header class="ci-head">
      <h3>{{ t('crm.prospecting.cardTitle') }}</h3>
      <p>{{ t('crm.prospecting.cardDescription') }}</p>
    </header>
    <label class="ci-payload">
      {{ t('crm.prospecting.cardPayload') }}
      <textarea v-model="rowsJson" rows="4" :placeholder="example" />
    </label>
    <div class="ci-actions">
      <button type="button" :disabled="!rowsJson" @click="runPreview">
        {{ t('crm.prospecting.cardPreview') }}
      </button>
      <button type="button" :disabled="!rowsJson" @click="runImport">
        {{ t('crm.prospecting.cardImport') }}
      </button>
      <button type="button" @click="reloadBatches">
        {{ t('crm.prospecting.cardRefresh') }}
      </button>
    </div>

    <div v-if="preview" class="ci-summary">
      {{ t('crm.prospecting.cardPreviewSummary', { total: preview.total, valid: preview.valid, duplicates: preview.duplicates }) }}
      <ul>
        <li v-for="row in preview.rows" :key="row.externalId">
          {{ row.contactName || row.companyName || row.externalId }}
          <span v-if="row.errors.length" class="ci-bad">{{ row.errors.join(', ') }}</span>
          <span v-else-if="row.duplicateReason" class="ci-warn">{{ row.duplicateReason }}</span>
        </li>
      </ul>
    </div>

    <label class="ci-batch">
      {{ t('crm.prospecting.cardBatch') }}
      <select v-model="batchId" @change="reloadRows">
        <option value="">{{ t('crm.prospecting.cardNoBatch') }}</option>
        <option v-for="batch in batches" :key="batch" :value="batch">{{ batch }}</option>
      </select>
    </label>
    <label class="ci-reason">
      {{ t('crm.prospecting.cardRejectReason') }}
      <input v-model="rejectReason" type="text">
    </label>
    <ul class="ci-rows">
      <li v-for="row in rows" :key="row.basicId">
        {{ row.contactName || row.companyName || row.externalId }} · {{ statusLabel(row.status) }}
        <span v-if="row.duplicateReason" class="ci-warn">{{ row.duplicateReason }}</span>
        <template v-if="row.status === 0">
          <button type="button" :disabled="!!row.duplicateReason" @click="decide(row.basicId, true)">
            {{ t('crm.prospecting.cardConfirm') }}
          </button>
          <button type="button" @click="decide(row.basicId, false)">
            {{ t('crm.prospecting.cardReject') }}
          </button>
        </template>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.card-import {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ci-payload,
.ci-batch,
.ci-reason {
  display: grid;
  gap: 0.25rem;
  font-size: 0.82rem;
}
.ci-payload textarea,
.ci-reason input,
.ci-batch select {
  width: 100%;
  font: inherit;
}
.ci-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.ci-summary,
.ci-rows {
  font-size: 0.82rem;
}
.ci-bad {
  color: var(--xh-danger, #c00);
}
.ci-warn {
  color: var(--xh-warning, #a60);
}
</style>
