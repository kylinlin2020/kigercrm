<script setup lang="ts">
import type { CrmCustomerImportPreviewDto, CrmCustomerImportResultDto } from '@/api/modules/crm/customer-import.types'
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
import { crmCustomerImportApi } from '@/api/modules/crm/customer-import'
import {
  CRM_CUSTOMER_IMPORT_FIELDS,

} from '@/api/modules/crm/customer-import.types'
import { XInput, XSelect } from '~/components'
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

/** 文件选择器只当入口用：选完即交给 loadCsv 解析，组件内不留文件，便于重复选同一份 */
const NO_FILES: File[] = []

const example = '[{"company":"Acme GmbH","site":"www.acme.com","region":"DE","sector":"Pumps"}]'

function payload() {
  const map: Record<string, string> = {}
  for (const item of mapping.value) {
    if (item.source.trim())
      map[item.source.trim()] = item.target
  }
  return { rowsJson: rowsJson.value, mapping: map, fileName: fileName.value || null }
}

async function loadCsv(details: { files: File[] }) {
  const file = details.files[0]
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
    <XhFieldRoot class="ci-rows">
      <XhFieldLabel>{{ t('crm.prospecting.customerImportRows') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rowsJson" type="textarea" :rows="4" :placeholder="example" />
      </XhFieldControl>
    </XhFieldRoot>
    <div class="ci-mapping">
      <header>{{ t('crm.prospecting.customerImportMapping') }}</header>
      <div v-for="(item, index) in mapping" :key="index" class="ci-map-row">
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.customerImportSource') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="item.source" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.customerImportMapping') }}</XhFieldLabel>
          <XhFieldControl>
            <XSelect
              v-model:value="item.target"
              :options="CRM_CUSTOMER_IMPORT_FIELDS.map(field => ({
                label: t(`crm.prospecting.customerImportField_${field.value}`),
                value: field.value,
              }))"
            />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="subtle" @click="removeMapping(index)">
          <XhButtonLabel>×</XhButtonLabel>
        </XhButton>
      </div>
      <XhButton variant="outline" @click="addMapping">
        <XhButtonLabel>{{ t('crm.prospecting.customerImportAddField') }}</XhButtonLabel>
      </XhButton>
    </div>
    <XhFieldRoot class="ci-file">
      <XhFieldLabel>{{ t('crm.prospecting.customerImportFileName') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="fileName" />
      </XhFieldControl>
    </XhFieldRoot>
    <XhFieldRoot class="ci-csv">
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
    <p v-if="csvNotice" class="ci-csv-notice">
      {{ csvNotice }}
    </p>
    <div class="ci-actions">
      <XhButton variant="outline" :disabled="!rowsJson" @click="runPreview">
        <XhButtonLabel>{{ t('crm.prospecting.customerImportPreview') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="outline" :disabled="!rowsJson" @click="runImport">
        <XhButtonLabel>{{ t('crm.prospecting.customerImportRun') }}</XhButtonLabel>
      </XhButton>
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
.ci-mapping {
  display: grid;
  gap: 0.3rem;
  font-size: 0.82rem;
}
.ci-map-row {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 0.3rem;
  align-items: end;
}
.ci-csv {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ci-csv-notice {
  font-size: 0.82rem;
  color: hsl(var(--success));
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
  color: hsl(var(--destructive));
}
.ci-warn {
  color: hsl(var(--warning));
}
</style>
