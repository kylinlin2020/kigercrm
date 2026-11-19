<script setup lang="ts">
import type {
  CrmDashboardOverviewDto,
  CrmDashboardReportDto,
} from '@/api/modules/crm/dashboard.types'
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmDashboardApi } from '@/api/modules/crm/dashboard'
import { toast } from '~/composables'

defineOptions({ name: 'CrmDashboardPanel' })

const { t } = useI18n()
const overview = ref<CrmDashboardOverviewDto | null>(null)
const funnel = ref<CrmDashboardReportDto | null>(null)
const sources = ref<CrmDashboardReportDto | null>(null)
const stages = ref<CrmDashboardReportDto | null>(null)

onMounted(async () => {
  try {
    const [summary, leadReport, sourceReport, stageReport] = await Promise.all([
      crmDashboardApi.overview(),
      crmDashboardApi.leadFunnel(),
      crmDashboardApi.prospectSources(),
      crmDashboardApi.opportunityStages(),
    ])
    overview.value = summary
    funnel.value = leadReport
    sources.value = sourceReport
    stages.value = stageReport
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.dashboardError'))
  }
})

function reportTitle(key: 'funnel' | 'sources' | 'stages') {
  return key === 'funnel'
    ? t('crm.prospecting.dashboardFunnel')
    : key === 'sources'
      ? t('crm.prospecting.dashboardSources')
      : t('crm.prospecting.dashboardStages')
}
</script>

<template>
  <section class="dashboard-panel">
    <header class="section-header">
      <div>
        <h2>{{ t('crm.prospecting.dashboardTitle') }}</h2>
        <p>{{ t('crm.prospecting.dashboardDescription') }}</p>
      </div>
    </header>

    <div v-if="overview" class="dash-metrics">
      <span>{{ t('crm.prospecting.dashboardLeads', { count: overview.leadsTotal }) }}</span>
      <span>{{ t('crm.prospecting.dashboardOpportunities', { count: overview.opportunitiesTotal }) }}</span>
      <span>{{ t('crm.prospecting.dashboardProspects', { count: overview.prospectsTotal }) }}</span>
      <span>{{ t('crm.prospecting.dashboardActiveRuns', { count: overview.activeRuns }) }}</span>
    </div>

    <div class="dash-grid">
      <div v-for="key in (['funnel', 'sources', 'stages'] as const)" :key="key" class="dash-card">
        <h3>{{ reportTitle(key) }}</h3>
        <div v-for="bucket in (key === 'funnel' ? funnel : key === 'sources' ? sources : stages)?.buckets ?? []" :key="bucket.key" class="dash-row">
          <span class="dash-label">{{ bucket.label }}</span>
          <svg class="dash-bar" viewBox="0 0 100 8" preserveAspectRatio="none" role="img" :aria-label="`${bucket.label}: ${bucket.count}`">
            <rect x="0" y="1" :width="Math.max(1, bucket.sharePercent)" height="6" rx="2" fill="var(--xh-fg-brand, #2563eb)" />
          </svg>
          <span class="dash-value">{{ bucket.count }} · {{ bucket.sharePercent }}%</span>
          <span v-if="bucket.amount" class="dash-value">{{ bucket.amount }}</span>
        </div>
        <p v-if="((key === 'funnel' ? funnel : key === 'sources' ? sources : stages)?.buckets.length ?? 0) === 0" class="dash-empty">
          {{ t('crm.prospecting.dashboardEmpty') }}
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.dashboard-panel {
  display: grid;
  gap: 0.75rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.dash-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.dash-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
  gap: 0.75rem;
}
.dash-card {
  padding: 0.5rem;
  border: 1px solid var(--xh-border-subtle, #ddd);
  border-radius: 0.5rem;
}
.dash-card h3 {
  margin: 0 0 0.4rem;
  font-size: 0.9rem;
}
.dash-row {
  display: grid;
  grid-template-columns: minmax(5rem, 1fr) minmax(3rem, 2fr) auto;
  align-items: center;
  gap: 0.4rem;
  padding: 0.2rem 0;
  font-size: 0.8rem;
}
.dash-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.dash-bar {
  width: 100%;
  height: 0.5rem;
}
.dash-value,
.dash-empty {
  color: var(--xh-fg-muted, #777);
}
</style>
