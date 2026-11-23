<script setup lang="ts">
import { XhPieChartRoot } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WidgetCard from '../WidgetCard.vue'
import { CURRENCY_COMPACT } from './chart-helpers'
import { useCrmDashboardData } from './use-crm-dashboard-data'

/** Real opportunity-stage mix from CRM (pie). */
defineOptions({ name: 'CrmStagesWidget' })

const { t, locale } = useI18n()
const { stages, loading } = useCrmDashboardData()

const rows = computed(() => (stages.value?.buckets ?? []).map(bucket => ({
  name: bucket.label,
  value: bucket.count,
})))
</script>

<template>
  <WidgetCard icon="lucide:chart-pie" :title="t('workbench.crm.stagesTitle')">
    <XhPieChartRoot
      v-if="rows.length"
      :data="rows"
      name-field="name"
      value-field="value"
      variant="pie"
      :format="CURRENCY_COMPACT"
      :locale="locale"
      :aria-label="t('workbench.crm.stagesDesc')"
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
