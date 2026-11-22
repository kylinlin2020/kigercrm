<script setup lang="ts">
import type {
  CrmTradeDocumentOrderLineDto,
  CrmTradeDocumentPreviewDto,
  CrmTradeDocumentRowDto,
} from '@/api/modules/crm/trade-document-import.types'
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
import { crmTradeDocumentImportApi } from '@/api/modules/crm/trade-document-import'
import { XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { csvToRows } from './csv-to-rows'

defineOptions({ name: 'CrmTradeDocumentImportPanel' })

const { t } = useI18n()
const rowsJson = ref('')
const salesOrderId = ref('')
const preview = ref<CrmTradeDocumentPreviewDto | null>(null)
const batches = ref<string[]>([])
const batchId = ref('')
const rows = ref<CrmTradeDocumentRowDto[]>([])
const orderLines = ref<Record<string, CrmTradeDocumentOrderLineDto[]>>({})
const mapped = ref<Record<string, Array<{ orderLineId: string, quantity: number }>>>({})
const rejectReason = ref('')

/** 文件选择器只当入口用：选完即交给 loadCsv 解析，组件内不留文件，便于重复选同一份 */
const NO_FILES: File[] = []

const example = '[{"externalId":"1","documentNumber":"INV-1","orderReference":"PO-1","currency":"USD","totalAmount":200,"lines":[{"sku":"SKU-1","quantity":2}]}]'

function statusLabel(status: number) {
  return [
    t('crm.prospecting.tradePending'),
    t('crm.prospecting.tradeConfirmed'),
    t('crm.prospecting.tradeRejected'),
  ][status] ?? String(status)
}

async function reloadRows() {
  if (!batchId.value) {
    rows.value = []
    return
  }
  try {
    rows.value = await crmTradeDocumentImportApi.rows(batchId.value)
    for (const row of rows.value) {
      if (row.status === 0 && !mapped.value[row.basicId]) {
        mapped.value[row.basicId] = row.lines.map(line => ({ orderLineId: '', quantity: line.quantity }))
      }
    }
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
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
    batches.value = await crmTradeDocumentImportApi.batches()
    if (!batches.value.includes(batchId.value))
      batchId.value = batches.value[0] ?? ''
    await reloadRows()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}

async function runPreview() {
  try {
    preview.value = await crmTradeDocumentImportApi.preview({
      rowsJson: rowsJson.value,
      salesOrderId: salesOrderId.value || null,
    })
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}

async function runImport() {
  try {
    const result = await crmTradeDocumentImportApi.import({
      rowsJson: rowsJson.value,
      salesOrderId: salesOrderId.value || null,
    })
    toast.success(t('crm.prospecting.tradeImported', { count: result.staged }))
    batchId.value = result.batchId
    await reloadBatches()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}

function mappedLine(rowId: string, index: number) {
  const list = mapped.value[rowId] ?? []
  while (list.length <= index) list.push({ orderLineId: '', quantity: 1 })
  mapped.value[rowId] = list
  const entry = list[index] ?? { orderLineId: '', quantity: 1 }
  list[index] = entry
  return entry
}

function setMapped(
  rowId: string,
  index: number,
  field: 'orderLineId' | 'quantity',
  value: string | number | Array<string | number> | null,
) {
  const target = mappedLine(rowId, index)
  if (field === 'quantity')
    target.quantity = Number(value)
  else target.orderLineId = Array.isArray(value) ? String(value[0] ?? '') : String(value ?? '')
}

async function loadOrderLines(row: CrmTradeDocumentRowDto) {
  if (!salesOrderId.value)
    return
  try {
    orderLines.value[row.basicId] = await crmTradeDocumentImportApi.orderLines(salesOrderId.value)
    const suggestions = orderLines.value[row.basicId] ?? []
    const current = mapped.value[row.basicId] ?? []
    current.forEach((item, index) => {
      const sku = row.lines[index]?.sku
      const match = suggestions.find(line => line.productCode?.toLowerCase() === sku?.toLowerCase())
      if (match)
        item.orderLineId = match.basicId
    })
    mapped.value[row.basicId] = [...current]
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}

async function confirm(row: CrmTradeDocumentRowDto) {
  try {
    await crmTradeDocumentImportApi.confirm({
      rowId: row.basicId,
      salesOrderId: salesOrderId.value,
      lines: (mapped.value[row.basicId] ?? []).map(item => ({
        orderLineId: item.orderLineId,
        quantity: Number(item.quantity),
      })),
    })
    await reloadRows()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}

async function reject(row: CrmTradeDocumentRowDto) {
  try {
    await crmTradeDocumentImportApi.reject({ rowId: row.basicId, reason: rejectReason.value || 'manual reject' })
    await reloadRows()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.tradeError'))
  }
}
</script>

<template>
  <section class="trade-import">
    <header class="ti-head">
      <h3>{{ t('crm.prospecting.tradeTitle') }}</h3>
      <p>{{ t('crm.prospecting.tradeDescription') }}</p>
    </header>
    <XhFieldRoot class="ti-payload">
      <XhFieldLabel>{{ t('crm.prospecting.tradePayload') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rowsJson" type="textarea" :rows="4" :placeholder="example" />
      </XhFieldControl>
    </XhFieldRoot>
    <XhFieldRoot class="ti-order">
      <XhFieldLabel>{{ t('crm.prospecting.tradeSalesOrderId') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="salesOrderId" />
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
    <div class="ti-actions">
      <XhButton variant="outline" :disabled="!rowsJson" @click="runPreview">
        <XhButtonLabel>{{ t('crm.prospecting.tradePreview') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="outline" :disabled="!rowsJson" @click="runImport">
        <XhButtonLabel>{{ t('crm.prospecting.tradeImport') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" @click="reloadBatches">
        <XhButtonLabel>{{ t('crm.prospecting.tradeRefresh') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div v-if="preview" class="ti-summary">
      {{ t('crm.prospecting.tradePreviewSummary', { total: preview.total, valid: preview.valid, duplicates: preview.duplicates }) }}
      <ul>
        <li v-for="row in preview.rows" :key="row.externalId">
          {{ row.documentNumber }} · {{ row.orderReference }}
          <span v-if="row.errors.length" class="ti-bad">{{ row.errors.join(', ') }}</span>
          <span v-else-if="row.duplicateReason" class="ti-warn">{{ row.duplicateReason }}</span>
        </li>
      </ul>
    </div>

    <XhFieldRoot class="ti-batch">
      <XhFieldLabel>{{ t('crm.prospecting.tradeBatch') }}</XhFieldLabel>
      <XhFieldControl>
        <XSelect
          v-model:value="batchId"
          :options="[
            { label: t('crm.prospecting.tradeNoBatch'), value: '' },
            ...batches.map(batch => ({ label: batch, value: batch })),
          ]"
          @update:value="reloadRows"
        />
      </XhFieldControl>
    </XhFieldRoot>
    <XhFieldRoot class="ti-reason">
      <XhFieldLabel>{{ t('crm.prospecting.tradeRejectReason') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rejectReason" />
      </XhFieldControl>
    </XhFieldRoot>

    <article v-for="row in rows" :key="row.basicId" class="ti-row">
      <header>{{ row.documentNumber }} · {{ row.orderReference }} · {{ statusLabel(row.status) }}</header>
      <div v-for="(line, index) in row.lines" :key="index" class="ti-line">
        {{ line.sku }} × {{ line.quantity }}
        <XSelect
          v-if="row.status === 0 && orderLines[row.basicId]"
          :value="mappedLine(row.basicId, index).orderLineId"
          :options="[
            { label: t('crm.prospecting.tradeSelectLine'), value: '' },
            ...(orderLines[row.basicId] ?? []).map(option => ({
              label: `${option.productCode || option.description} (${option.availableQuantity})`,
              value: option.basicId,
            })),
          ]"
          @update:value="(value: string | number | Array<string | number> | null) => setMapped(row.basicId, index, 'orderLineId', value)"
        />
        <XInput
          v-if="row.status === 0"
          :value="String(mappedLine(row.basicId, index).quantity)"
          inputmode="decimal"
          @update:value="value => setMapped(row.basicId, index, 'quantity', value)"
        />
      </div>
      <span v-if="row.duplicateReason" class="ti-warn">{{ row.duplicateReason }}</span>
      <div v-if="row.status === 0" class="ti-actions">
        <XhButton variant="subtle" :disabled="!salesOrderId" @click="loadOrderLines(row)">
          <XhButtonLabel>{{ t('crm.prospecting.tradeLoadLines') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="outline" :disabled="!!row.duplicateReason" @click="confirm(row)">
          <XhButtonLabel>{{ t('crm.prospecting.tradeConfirm') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" @click="reject(row)">
          <XhButtonLabel>{{ t('crm.prospecting.tradeReject') }}</XhButtonLabel>
        </XhButton>
      </div>
    </article>
  </section>
</template>

<style scoped>
.csv-load {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}

.trade-import {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ti-payload,
.ti-order,
.ti-batch,
.ti-reason {
  display: grid;
  gap: 0.25rem;
  font-size: 0.82rem;
}
.ti-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.ti-summary,
.ti-row {
  font-size: 0.82rem;
}
.ti-row {
  display: grid;
  gap: 0.3rem;
  padding: 0.5rem 0;
  border-top: 1px solid hsl(var(--border));
}
.ti-line {
  display: grid;
  grid-template-columns: minmax(8rem, 1fr) minmax(8rem, 1fr) 5rem;
  gap: 0.3rem;
  align-items: center;
}
.ti-bad {
  color: hsl(var(--destructive));
}
.ti-warn {
  color: hsl(var(--warning));
}
</style>
