<script setup lang="ts">
import type {
  CrmBusinessCardPreviewDto,
  CrmBusinessCardRowDto,
} from '@/api/modules/crm/business-card-import.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
  XhFileUploadHiddenInput,
  XhFileUploadRoot,
  XhFileUploadTrigger,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmBusinessCardImportApi } from '@/api/modules/crm/business-card-import'
import { XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { csvToRows } from './csv-to-rows'

defineOptions({ name: 'CrmBusinessCardImportPanel' })

const { t } = useI18n()
const rowsJson = ref('')
const preview = ref<CrmBusinessCardPreviewDto | null>(null)
const batches = ref<string[]>([])
const batchId = ref('')
const rows = ref<CrmBusinessCardRowDto[]>([])
const rejectReason = ref('')

/** 文件选择器只当入口用：选完即交给 loadCsv 解析，组件内不留文件，便于重复选同一份 */
const NO_FILES: File[] = []

const example = '[{"externalId":"1","contactName":"Alice","companyName":"Acme","email":"alice@acme.com","phone":"13800138000"}]'

function statusLabel(status: number) {
  return [
    t('crm.prospecting.cardPending'),
    t('crm.prospecting.cardConfirmed'),
    t('crm.prospecting.cardRejected'),
  ][status] ?? String(status)
}

async function loadCsv(details: { files: File[] }) {
  const file = details.files[0]
  if (!file)
    return
  try {
    const converted = csvToRows(await file.text())
    rowsJson.value = converted.rowsJson
    toast.success(t('crm.prospecting.csvLoaded', { count: converted.rowCount }))
  }
  catch {
    toast.danger(t('crm.prospecting.csvError'))
  }
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
    <XhFieldRoot class="ci-payload">
      <XhFieldLabel>{{ t('crm.prospecting.cardPayload') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rowsJson" type="textarea" :rows="4" :placeholder="example" />
      </XhFieldControl>
    </XhFieldRoot>
    <XhFieldRoot class="csv-load">
      <XhFieldLabel>{{ t('crm.prospecting.csvLoad') }}</XhFieldLabel>
      <XhFieldControl>
        <XhFileUploadRoot
          accept=".csv,text/csv"
          :files="NO_FILES"
          @file-accept="loadCsv"
        >
          <XhFileUploadHiddenInput />
          <XhFileUploadTrigger as-child>
            <XhButton variant="subtle">
              <XhButtonLabel>{{ t('crm.prospecting.csvLoad') }}</XhButtonLabel>
            </XhButton>
          </XhFileUploadTrigger>
        </XhFileUploadRoot>
      </XhFieldControl>
      <small>{{ t('crm.prospecting.csvHint') }}</small>
    </XhFieldRoot>
    <div class="ci-actions">
      <XhButton variant="outline" :disabled="!rowsJson" @click="runPreview">
        <XhButtonLabel>{{ t('crm.prospecting.cardPreview') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="outline" :disabled="!rowsJson" @click="runImport">
        <XhButtonLabel>{{ t('crm.prospecting.cardImport') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" @click="reloadBatches">
        <XhButtonLabel>{{ t('crm.prospecting.cardRefresh') }}</XhButtonLabel>
      </XhButton>
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

    <XhFieldRoot class="ci-batch">
      <XhFieldLabel>{{ t('crm.prospecting.cardBatch') }}</XhFieldLabel>
      <XhFieldControl>
        <XSelect
          v-model:value="batchId"
          :options="[
            { label: t('crm.prospecting.cardNoBatch'), value: '' },
            ...batches.map(batch => ({ label: batch, value: batch })),
          ]"
          @update:value="reloadRows"
        />
      </XhFieldControl>
    </XhFieldRoot>
    <XhFieldRoot class="ci-reason">
      <XhFieldLabel>{{ t('crm.prospecting.cardRejectReason') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rejectReason" />
      </XhFieldControl>
    </XhFieldRoot>
    <ul class="ci-rows">
      <li v-for="row in rows" :key="row.basicId">
        {{ row.contactName || row.companyName || row.externalId }} · {{ statusLabel(row.status) }}
        <span v-if="row.duplicateReason" class="ci-warn">{{ row.duplicateReason }}</span>
        <template v-if="row.status === 0">
          <XhButton variant="outline" :disabled="!!row.duplicateReason" @click="decide(row.basicId, true)">
            <XhButtonLabel>{{ t('crm.prospecting.cardConfirm') }}</XhButtonLabel>
          </XhButton>
          <XhButton variant="subtle" @click="decide(row.basicId, false)">
            <XhButtonLabel>{{ t('crm.prospecting.cardReject') }}</XhButtonLabel>
          </XhButton>
        </template>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.csv-load {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}

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
  color: hsl(var(--destructive));
}
.ci-warn {
  color: hsl(var(--warning));
}
</style>
