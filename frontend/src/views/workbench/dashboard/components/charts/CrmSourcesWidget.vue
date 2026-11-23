<script setup lang="ts">
import { XhPieChartRoot } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WidgetCard from '../WidgetCard.vue'
import { CURRENCY_COMPACT } from './chart-helpers'
import { useCrmDashboardData } from './use-crm-dashboard-data'

/** Real prospect-source mix from CRM (donut). */
defineOptions({ name: 'CrmSourcesWidget' })

const { t, locale } = useI18n()
const { sources, loading } = useCrmDashboardData()

const rows = computed(() => (sources.value?.buckets ?? []).map(bucket => ({
  name: bucket.label,
  value: bucket.count,
})))
</script>

<template>
  <WidgetCard icon="lucide:pie-chart" :title="t('workbench.crm.sourcesTitle')">
    <XhPieChartRoot
      v-if="rows.length"
      :data="rows"
      name-field="name"
      value-field="value"
      variant="donut"
      :format="CURRENCY_COMPACT"
      :locale="locale"
      :aria-label="t('workbench.crm.sourcesDesc')"
    />
    <p v-else-if="!loading" class="crm-empty">
      {{ t('workbench.crm.empty') }}
    </p>
  </WidgetCard>
</template>

<style scoped>
.crm-empty {
  color: var(--xh-fg-muted);
  font-size: var(--xh-font-size-sm);
}
</style>
