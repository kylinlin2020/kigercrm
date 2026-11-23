<script setup lang="ts">
import { XhFunnelChartRoot } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WidgetCard from '../WidgetCard.vue'
import { useCrmDashboardData } from './use-crm-dashboard-data'

/** Real lead funnel from CRM. */
defineOptions({ name: 'CrmFunnelWidget' })

const { t, locale } = useI18n()
const { funnel, loading } = useCrmDashboardData()

const rows = computed(() => (funnel.value?.buckets ?? []).map(bucket => ({
  stage: bucket.label,
  users: bucket.count,
})))
</script>

<template>
  <WidgetCard icon="lucide:filter" :title="t('workbench.crm.funnelTitle')">
    <XhFunnelChartRoot
      v-if="rows.length"
      :data="rows"
      name-field="stage"
      value-field="users"
      shape="trapezoid"
      conversion="previous"
      :format="{ notation: 'compact' }"
      :locale="locale"
      :aria-label="t('workbench.crm.funnelDesc')"
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
