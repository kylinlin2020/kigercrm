<script setup lang="ts">
import { XhStatisticLabel, XhStatisticRoot, XhStatisticValue } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WidgetCard from '../WidgetCard.vue'
import { useFormatters } from './chart-helpers'
import { useCrmDashboardData } from './use-crm-dashboard-data'

/** Real CRM overview metrics: leads, opportunities, prospects, active runs and pipeline amount. */
defineOptions({ name: 'CrmOverviewWidget' })

const { t } = useI18n()
const formatters = useFormatters()
const { overview, loading } = useCrmDashboardData()

const cards = computed(() => {
  const data = overview.value
  const rows = [
    { key: 'leads', label: t('workbench.crm.leads'), value: data?.leadsTotal ?? 0, format: formatters.value.number },
    { key: 'opportunities', label: t('workbench.crm.opportunities'), value: data?.opportunitiesTotal ?? 0, format: formatters.value.number },
    { key: 'prospects', label: t('workbench.crm.prospects'), value: data?.prospectsTotal ?? 0, format: formatters.value.number },
    { key: 'activeRuns', label: t('workbench.crm.activeRuns'), value: data?.activeRuns ?? 0, format: formatters.value.number },
    { key: 'amount', label: t('workbench.crm.amount'), value: data?.opportunityAmount ?? 0, format: formatters.value.currencyFull },
  ]
  return rows.map(row => ({ ...row, display: row.format(row.value) }))
})
</script>

<template>
  <WidgetCard icon="lucide:gauge" :title="t('workbench.crm.overviewTitle')">
    <div class="crm-overview">
      <div v-for="card in cards" :key="card.key" class="crm-metric">
        <XhStatisticRoot size="sm">
          <XhStatisticLabel>{{ card.label }}</XhStatisticLabel>
          <XhStatisticValue>{{ card.display }}</XhStatisticValue>
        </XhStatisticRoot>
      </div>
      <p v-if="!loading && !overview" class="crm-empty">
        {{ t('workbench.crm.empty') }}
      </p>
    </div>
  </WidgetCard>
</template>

<style scoped>
.crm-overview {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr));
  gap: var(--xh-space-3);
}
.crm-metric {
  padding: var(--xh-space-3);
  border: var(--xh-stroke-thin) solid var(--xh-border-subtle);
  border-radius: var(--xh-shape-surface);
  background-color: var(--xh-bg-surface);
}
.crm-empty {
  color: var(--xh-fg-muted);
  font-size: var(--xh-font-size-sm);
}
</style>
