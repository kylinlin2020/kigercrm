<script setup lang="ts">
import type {
  CrmTradeDocumentOrderLineDto,
  CrmTradeDocumentPreviewDto,
  CrmTradeDocumentRowDto,
} from '@/api/modules/crm/trade-document-import.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmTradeDocumentImportApi } from '@/api/modules/crm/trade-document-import'
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

async function loadCsv(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
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
  input.value = ''
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

function setMapped(rowId: string, index: number, field: 'orderLineId' | 'quantity', event: Event) {
  const raw = (event.target as HTMLInputElement | HTMLSelectElement).value
  const target = mappedLine(rowId, index)
  if (field === 'quantity')
    target.quantity = Number(raw)
  else target.orderLineId = raw
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
    <label class="ti-payload">
      {{ t('crm.prospecting.tradePayload') }}
      <textarea v-model="rowsJson" rows="4" :placeholder="example" />
    </label>
    <label class="ti-order">
      {{ t('crm.prospecting.tradeSalesOrderId') }}
      <input v-model="salesOrderId" type="text">
    </label>
    <label class="csv-load">
      {{ t('crm.prospecting.csvLoad') }}
      <input type="file" accept=".csv,text/csv" @change="loadCsv">
      <small>{{ t('crm.prospecting.csvHint') }}</small>
    </label>
    <div class="ti-actions">
      <button type="button" :disabled="!rowsJson" @click="runPreview">
        {{ t('crm.prospecting.tradePreview') }}
      </button>
      <button type="button" :disabled="!rowsJson" @click="runImport">
        {{ t('crm.prospecting.tradeImport') }}
      </button>
      <button type="button" @click="reloadBatches">
        {{ t('crm.prospecting.tradeRefresh') }}
      </button>
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

    <label class="ti-batch">
      {{ t('crm.prospecting.tradeBatch') }}
      <select v-model="batchId" @change="reloadRows">
        <option value="">{{ t('crm.prospecting.tradeNoBatch') }}</option>
        <option v-for="batch in batches" :key="batch" :value="batch">{{ batch }}</option>
      </select>
    </label>
    <label class="ti-reason">
      {{ t('crm.prospecting.tradeRejectReason') }}
      <input v-model="rejectReason" type="text">
    </label>

    <article v-for="row in rows" :key="row.basicId" class="ti-row">
      <header>{{ row.documentNumber }} · {{ row.orderReference }} · {{ statusLabel(row.status) }}</header>
      <div v-for="(line, index) in row.lines" :key="index" class="ti-line">
        {{ line.sku }} × {{ line.quantity }}
        <select v-if="row.status === 0 && orderLines[row.basicId]" :value="mappedLine(row.basicId, index).orderLineId" @change="setMapped(row.basicId, index, 'orderLineId', $event)">
          <option value="">
            {{ t('crm.prospecting.tradeSelectLine') }}
          </option>
          <option v-for="option in orderLines[row.basicId]" :key="option.basicId" :value="option.basicId">
            {{ option.productCode || option.description }} ({{ option.availableQuantity }})
          </option>
        </select>
        <input v-if="row.status === 0" :value="mappedLine(row.basicId, index).quantity" type="number" min="0" step="0.0001" @change="setMapped(row.basicId, index, 'quantity', $event)">
      </div>
      <span v-if="row.duplicateReason" class="ti-warn">{{ row.duplicateReason }}</span>
      <div v-if="row.status === 0" class="ti-actions">
        <button type="button" :disabled="!salesOrderId" @click="loadOrderLines(row)">
          {{ t('crm.prospecting.tradeLoadLines') }}
        </button>
        <button type="button" :disabled="!!row.duplicateReason" @click="confirm(row)">
          {{ t('crm.prospecting.tradeConfirm') }}
        </button>
        <button type="button" @click="reject(row)">
          {{ t('crm.prospecting.tradeReject') }}
        </button>
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
.ti-payload textarea,
.ti-order input,
.ti-batch select,
.ti-reason input,
.ti-line select,
.ti-line input {
  width: 100%;
  font: inherit;
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
  border-top: 1px solid var(--xh-border-subtle, #ddd);
}
.ti-line {
  display: grid;
  grid-template-columns: minmax(8rem, 1fr) minmax(8rem, 1fr) 5rem;
  gap: 0.3rem;
  align-items: center;
}
.ti-bad {
  color: var(--xh-danger, #c00);
}
.ti-warn {
  color: var(--xh-warning, #a60);
}
</style>
