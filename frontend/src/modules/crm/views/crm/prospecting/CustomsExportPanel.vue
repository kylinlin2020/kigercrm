<script setup lang="ts">
import type {
  CrmCustomsFilterResultDto,
  CrmCustomsValidationDto,
} from '@/api/modules/crm/customs-export.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmCustomsExportApi } from '@/api/modules/crm/customs-export'
import { XInput } from '~/components'
import { toast } from '~/composables'

defineOptions({ name: 'CrmCustomsExportPanel' })

const { t } = useI18n()
const rowsJson = ref('')
const industry = ref('')
const hsPrefix = ref('')
const countryCode = ref('')
const validation = ref<CrmCustomsValidationDto | null>(null)
const filtered = ref<CrmCustomsFilterResultDto | null>(null)

const example = '[{"companyName":"Acme GmbH","countryCode":"DE","hsCode":"841370","industry":"Pumps","exportDate":"2026-11-20","amount":1200}]'

function payload() {
  return {
    rowsJson: rowsJson.value,
    filter: {
      industry: industry.value || null,
      hsPrefix: hsPrefix.value || null,
      countryCode: countryCode.value || null,
    },
  }
}

async function runValidate() {
  try {
    validation.value = await crmCustomsExportApi.validate(payload())
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.customsError'))
  }
}

async function runFilter() {
  try {
    filtered.value = await crmCustomsExportApi.filter(payload())
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.customsError'))
  }
}

async function runExport() {
  try {
    const result = await crmCustomsExportApi.export(payload())
    const blob = new Blob([`\uFEFF${result.csv}`], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'customs-export.csv'
    link.click()
    URL.revokeObjectURL(url)
    toast.success(t('crm.prospecting.customsExported', { count: result.exported }))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.customsError'))
  }
}
</script>

<template>
  <section class="customs-export">
    <header class="ce-head">
      <h3>{{ t('crm.prospecting.customsTitle') }}</h3>
      <p>{{ t('crm.prospecting.customsDescription') }}</p>
    </header>
    <XhFieldRoot class="ce-payload">
      <XhFieldLabel>{{ t('crm.prospecting.customsPayload') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="rowsJson" type="textarea" :rows="4" :placeholder="example" />
      </XhFieldControl>
    </XhFieldRoot>
    <div class="ce-filters">
      <XhFieldRoot class="ce-filter">
        <XhFieldLabel>{{ t('crm.prospecting.customsIndustry') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="industry" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="ce-filter">
        <XhFieldLabel>{{ t('crm.prospecting.customsHsPrefix') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="hsPrefix" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="ce-filter">
        <XhFieldLabel>{{ t('crm.prospecting.customsCountry') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="countryCode" />
        </XhFieldControl>
      </XhFieldRoot>
    </div>
    <div class="ce-actions">
      <XhButton variant="outline" :disabled="!rowsJson" @click="runValidate">
        <XhButtonLabel>{{ t('crm.prospecting.customsValidate') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="outline" :disabled="!rowsJson" @click="runFilter">
        <XhButtonLabel>{{ t('crm.prospecting.customsFilter') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="outline" :disabled="!rowsJson" @click="runExport">
        <XhButtonLabel>{{ t('crm.prospecting.customsExport') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div v-if="validation" class="ce-result">
      {{ t('crm.prospecting.customsValidationSummary', { total: validation.total, valid: validation.valid }) }}
      <ul>
        <li v-for="(row, index) in validation.rows" :key="index">
          {{ row.companyName }} · {{ row.hsCode }}
          <span v-if="row.errors.length" class="ce-bad">{{ row.errors.join(', ') }}</span>
        </li>
      </ul>
    </div>
    <div v-if="filtered" class="ce-result">
      {{ t('crm.prospecting.customsFilterSummary', { total: filtered.total, matched: filtered.matched }) }}
      <ul>
        <li v-for="(row, index) in filtered.rows" :key="index">
          {{ row.companyName }} · {{ row.countryCode }} · {{ row.hsCode }} · {{ row.amount }}
        </li>
      </ul>
    </div>
  </section>
</template>

<style scoped>
.customs-export {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ce-payload {
  display: grid;
  gap: 0.25rem;
  font-size: 0.82rem;
}
.ce-filters {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  gap: 0.5rem;
}
.ce-filters .ce-filter {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ce-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.ce-result {
  font-size: 0.82rem;
}
.ce-bad {
  color: hsl(var(--destructive));
}
</style>
