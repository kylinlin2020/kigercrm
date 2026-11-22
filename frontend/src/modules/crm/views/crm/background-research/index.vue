<script setup lang="ts">
import type { CrmBackgroundResearchDetailDto, CrmBackgroundResearchReportDto } from '@/api/modules/crm/background-research.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmBackgroundResearchApi } from '@/api/modules/crm/background-research'
import { CrmMarketOpportunityStatus, CrmResearchSource } from '@/api/modules/crm/background-research.types'
import { XInput } from '~/components'
import { toast } from '~/composables'

defineOptions({ name: 'CrmBackgroundResearchPage' })

const { t } = useI18n()
const customerId = ref('')
const detail = ref<CrmBackgroundResearchDetailDto | null>(null)
const history = ref<CrmBackgroundResearchReportDto[]>([])
const working = ref(false)

function factLabel(key: string) {
  return t(`crm.backgroundResearch.fact.${key}`)
}

function sourceLabel(source: CrmResearchSource) {
  return t(`crm.backgroundResearch.source.${CrmResearchSource[source]}`)
}

function opportunityStatusLabel(status: CrmMarketOpportunityStatus) {
  return t(`crm.backgroundResearch.opportunityStatus.${CrmMarketOpportunityStatus[status]}`)
}

async function exportReport(format: string) {
  if (!customerId.value)
    return
  try {
    const file = await crmBackgroundResearchApi.export(customerId.value, format)
    const blob = new Blob([file.content], { type: file.contentType })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = file.fileName
    link.click()
    URL.revokeObjectURL(url)
    toast.success(t('crm.backgroundResearch.exported'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.backgroundResearch.error'))
  }
}

async function loadHistory() {
  if (!customerId.value)
    return
  try {
    history.value = await crmBackgroundResearchApi.history(customerId.value)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.backgroundResearch.error'))
  }
}

async function generate() {
  if (!customerId.value)
    return
  working.value = true
  try {
    detail.value = await crmBackgroundResearchApi.generate(customerId.value)
    toast.success(t('crm.backgroundResearch.generated'))
    await loadHistory()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.backgroundResearch.error'))
  }
  finally {
    working.value = false
  }
}

async function loadCurrent() {
  if (!customerId.value)
    return
  try {
    detail.value = await crmBackgroundResearchApi.current(customerId.value)
    if (!detail.value)
      toast.info(t('crm.backgroundResearch.noReport'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.backgroundResearch.error'))
  }
}

async function review(opportunityId: string, status: CrmMarketOpportunityStatus) {
  try {
    const updated = await crmBackgroundResearchApi.review({ opportunityId, status })
    if (detail.value) {
      const target = detail.value.opportunities.find(item => item.basicId === opportunityId)
      if (target)
        target.status = updated.status
    }
    toast.success(t('crm.backgroundResearch.reviewed'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.backgroundResearch.error'))
  }
}
</script>

<template>
  <main class="background-research-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.backgroundResearch.title') }}</h1>
        <p>{{ t('crm.backgroundResearch.description') }}</p>
      </div>
    </header>

    <section class="research-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.backgroundResearch.customerId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="customerId" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!customerId || working" @click="generate">
        <XhButtonLabel>{{ t('crm.backgroundResearch.generate') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="!customerId" @click="loadCurrent">
        <XhButtonLabel>{{ t('crm.backgroundResearch.current') }}</XhButtonLabel>
      </XhButton>
    </section>

    <template v-if="detail">
      <section class="research-section">
        <h2>{{ t('crm.backgroundResearch.reportTitle') }}</h2>
        <p class="research-meta">
          {{ detail.report.customerName }} · {{ detail.report.generatedAtUtc }} ·
          {{ t('crm.backgroundResearch.evidenceCount') }} {{ detail.report.evidenceCount }} ·
          {{ t('crm.backgroundResearch.opportunityCount') }} {{ detail.report.opportunityCount }}
        </p>
        <div class="research-actions">
          <XhButton variant="subtle" @click="exportReport('markdown')">
            <XhButtonLabel>{{ t('crm.backgroundResearch.exportMarkdown') }}</XhButtonLabel>
          </XhButton>
          <XhButton variant="subtle" @click="exportReport('json')">
            <XhButtonLabel>{{ t('crm.backgroundResearch.exportJson') }}</XhButtonLabel>
          </XhButton>
        </div>
      </section>

      <section class="research-section">
        <h2>{{ t('crm.backgroundResearch.evidenceTitle') }}</h2>
        <p v-if="detail.evidence.length === 0">
          {{ t('crm.backgroundResearch.noEvidence') }}
        </p>
        <div v-for="item in detail.evidence" :key="item.basicId" class="research-row">
          <span class="research-source">{{ sourceLabel(item.source) }}</span>
          <span>{{ factLabel(item.factKey) }}: {{ item.factValue }}</span>
          <span class="research-confidence">{{ item.confidence }}</span>
        </div>
      </section>

      <section class="research-section">
        <h2>{{ t('crm.backgroundResearch.opportunityTitle') }}</h2>
        <p v-if="detail.opportunities.length === 0">
          {{ t('crm.backgroundResearch.noOpportunities') }}
        </p>
        <div v-for="item in detail.opportunities" :key="item.basicId" class="research-row">
          <span>{{ item.title }} · {{ item.factValue }}</span>
          <span class="research-score">{{ item.priorityScore }}</span>
          <span>{{ opportunityStatusLabel(item.status) }}</span>
          <template v-if="item.status === CrmMarketOpportunityStatus.Proposed">
            <XhButton variant="outline" @click="review(item.basicId, CrmMarketOpportunityStatus.Confirmed)">
              <XhButtonLabel>{{ t('crm.backgroundResearch.confirm') }}</XhButtonLabel>
            </XhButton>
            <XhButton variant="subtle" @click="review(item.basicId, CrmMarketOpportunityStatus.Dismissed)">
              <XhButtonLabel>{{ t('crm.backgroundResearch.dismiss') }}</XhButtonLabel>
            </XhButton>
          </template>
        </div>
      </section>
    </template>
    <p v-else class="research-empty">
      {{ t('crm.backgroundResearch.noReport') }}
    </p>

    <section class="research-section">
      <h2>{{ t('crm.backgroundResearch.historyTitle') }}</h2>
      <p v-if="history.length === 0">
        {{ t('crm.backgroundResearch.noHistory') }}
      </p>
      <div v-for="item in history" :key="item.basicId" class="research-row">
        <span>{{ item.generatedAtUtc }} · {{ item.evidenceCount }} · {{ item.opportunityCount }}</span>
      </div>
    </section>
  </main>
</template>

<style scoped>
.background-research-page {
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.35rem;
}
.page-header p,
.research-meta,
.research-empty {
  margin: 0.25rem 0 0;
  color: hsl(var(--muted-foreground));
}
.research-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.research-section {
  display: grid;
  gap: 0.4rem;
}
.research-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.research-section h2 {
  margin: 0;
  font-size: 1rem;
}
.research-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.research-source,
.research-confidence,
.research-score {
  color: hsl(var(--muted-foreground));
}
</style>
