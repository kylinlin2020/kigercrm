<script setup lang="ts">
import type { CrmCustomerImportPreviewDto, CrmCustomerImportResultDto } from '@/api/modules/crm/customer-import.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmCustomerImportApi } from '@/api/modules/crm/customer-import'
import {
  CRM_CUSTOMER_IMPORT_FIELDS,

} from '@/api/modules/crm/customer-import.types'
import { toast } from '~/composables'
import { csvToRows, suggestCustomerMapping } from './csv-to-rows'

defineOptions({ name: 'CrmCustomerImportPanel' })

const { t } = useI18n()
const rowsJson = ref('')
const fileName = ref('customers.json')
const mapping = ref<Array<{ source: string, target: string }>>([
  { source: 'company', target: 'name' },
  { source: 'site', target: 'website' },
  { source: 'region', target: 'countryRegion' },
])
const preview = ref<CrmCustomerImportPreviewDto | null>(null)
const result = ref<CrmCustomerImportResultDto | null>(null)
const csvNotice = ref('')

const example = '[{"company":"Acme GmbH","site":"www.acme.com","region":"DE","sector":"Pumps"}]'

function payload() {
  const map: Record<string, string> = {}
  for (const item of mapping.value) {
    if (item.source.trim())
      map[item.source.trim()] = item.target
  }
  return { rowsJson: rowsJson.value, mapping: map, fileName: fileName.value || null }
}

async function loadCsv(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file)
    return
  try {
    const converted = csvToRows(await file.text())
    rowsJson.value = converted.rowsJson
    const suggested = suggestCustomerMapping(converted.headers)
    if (suggested.length > 0)
      mapping.value = suggested
    csvNotice.value = t('crm.prospecting.csvLoaded', { count: converted.rowCount })
  }
  catch {
    csvNotice.value = ''
    toast.danger(t('crm.prospecting.csvError'))
  }
  input.value = ''
}

async function runPreview() {
  try {
    preview.value = await crmCustomerImportApi.preview(payload())
    result.value = null
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.customerImportError'))
  }
}

async function runImport() {
  try {
    result.value = await crmCustomerImportApi.import(payload())
    toast.success(t('crm.prospecting.customerImportDone', { count: result.value.created }))
    await runPreview()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.customerImportError'))
  }
}

function addMapping() {
  mapping.value.push({ source: '', target: 'notes' })
}

function removeMapping(index: number) {
  mapping.value.splice(index, 1)
}
</script>

<template>
  <section class="customer-import">
    <header class="ci-head">
      <h3>{{ t('crm.prospecting.customerImportTitle') }}</h3>
      <p>{{ t('crm.prospecting.customerImportDescription') }}</p>
    </header>
    <label class="ci-rows">
      {{ t('crm.prospecting.customerImportRows') }}
      <textarea v-model="rowsJson" rows="4" :placeholder="example" />
    </label>
    <div class="ci-mapping">
      <header>{{ t('crm.prospecting.customerImportMapping') }}</header>
      <div v-for="(item, index) in mapping" :key="index" class="ci-map-row">
        <input v-model="item.source" type="text" :placeholder="t('crm.prospecting.customerImportSource')">
        <select v-model="item.target">
          <option v-for="field in CRM_CUSTOMER_IMPORT_FIELDS" :key="field.value" :value="field.value">
            {{ t(`crm.prospecting.customerImportField_${field.value}`) }}
          </option>
        </select>
        <button type="button" @click="removeMapping(index)">
          ×
        </button>
      </div>
      <button type="button" @click="addMapping">
        {{ t('crm.prospecting.customerImportAddField') }}
      </button>
    </div>
    <label class="ci-file">
      {{ t('crm.prospecting.customerImportFileName') }}
      <input v-model="fileName" type="text">
    </label>
    <label class="ci-csv">
      {{ t('crm.prospecting.csvLoad') }}
      <input type="file" accept=".csv,text/csv" @change="loadCsv">
      <small>{{ t('crm.prospecting.csvHint') }}</small>
    </label>
    <p v-if="csvNotice" class="ci-csv-notice">
      {{ csvNotice }}
    </p>
    <div class="ci-actions">
      <button type="button" :disabled="!rowsJson" @click="runPreview">
        {{ t('crm.prospecting.customerImportPreview') }}
      </button>
      <button type="button" :disabled="!rowsJson" @click="runImport">
        {{ t('crm.prospecting.customerImportRun') }}
      </button>
    </div>

    <div v-if="preview" class="ci-result">
      {{ t('crm.prospecting.customerImportPreviewSummary', { total: preview.total, valid: preview.valid, duplicates: preview.duplicates }) }}
      <ul>
        <li v-for="row in preview.rows" :key="row.externalId">
          {{ row.name || row.externalId }}
          <span v-if="row.errors.length" class="ci-bad">{{ row.errors.join(', ') }}</span>
          <span v-else-if="row.duplicateReason" class="ci-warn">{{ row.duplicateReason }}</span>
        </li>
      </ul>
    </div>
    <p v-if="result" class="ci-done">
      {{ t('crm.prospecting.customerImportResultSummary', { created: result.created, invalid: result.invalid, duplicates: result.duplicates, history: result.importHistoryId }) }}
    </p>
  </section>
</template>

<style scoped>
.customer-import {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ci-rows,
.ci-file {
  display: grid;
  gap: 0.25rem;
  font-size: 0.82rem;
}
.ci-rows textarea,
.ci-file input {
  width: 100%;
  font: inherit;
}
.ci-mapping {
  display: grid;
  gap: 0.3rem;
  font-size: 0.82rem;
}
.ci-map-row {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 0.3rem;
}
.ci-map-row input,
.ci-map-row select {
  width: 100%;
  font: inherit;
}
.ci-csv {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ci-csv-notice {
  font-size: 0.82rem;
  color: var(--xh-success, #2a0);
}
.ci-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.ci-result,
.ci-done {
  font-size: 0.82rem;
}
.ci-bad {
  color: var(--xh-danger, #c00);
}
.ci-warn {
  color: var(--xh-warning, #a60);
}
</style>
