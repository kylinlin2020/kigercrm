<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WidgetCard from '../WidgetCard.vue'
import { useCrmDashboardData } from './use-crm-dashboard-data'

/** Real follow-up health from CRM, with deterministic i18n labels for the bucket keys. */
defineOptions({ name: 'CrmFollowUpWidget' })

const { t } = useI18n()
const { followUp, loading } = useCrmDashboardData()

const rows = computed(() => {
  const buckets = followUp.value?.buckets ?? []
  const max = Math.max(1, ...buckets.map(bucket => bucket.count))
  return buckets.map(bucket => ({
    key: bucket.key,
    label: t(`workbench.crm.health_${bucket.key}`, [bucket.label]),
    count: bucket.count,
    share: `${bucket.sharePercent}%`,
    width: `${Math.max(1, (bucket.count / max) * 100)}%`,
  }))
})
</script>

<template>
  <WidgetCard icon="lucide:heart-pulse" :title="t('workbench.crm.followUpTitle')">
    <div v-if="rows.length" class="crm-rows">
      <div v-for="row in rows" :key="row.key" class="crm-row">
        <span class="crm-label">{{ row.label }}</span>
        <span class="crm-track"><span class="crm-bar" :style="{ width: row.width }" /></span>
        <span class="crm-value">{{ row.count }} · {{ row.share }}</span>
      </div>
    </div>
    <p v-else-if="!loading" class="crm-empty">
      {{ t('workbench.crm.empty') }}
    </p>
  </WidgetCard>
</template>

<style scoped>
.crm-rows {
  display: grid;
  gap: var(--xh-space-2);
}
.crm-row {
  display: grid;
  grid-template-columns: minmax(5rem, 1fr) minmax(3rem, 2fr) auto;
  align-items: center;
  gap: var(--xh-space-2);
  font-size: var(--xh-font-size-sm);
}
.crm-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.crm-track {
  display: block;
  block-size: var(--xh-space-2);
  border-radius: var(--xh-radius-full);
  background-color: var(--xh-bg-subtle);
  overflow: hidden;
}
.crm-bar {
  display: block;
  block-size: 100%;
  border-radius: var(--xh-radius-full);
  background-color: var(--xh-fg-brand);
}
.crm-value {
  color: var(--xh-fg-muted);
  white-space: nowrap;
}
.crm-empty {
  color: var(--xh-fg-muted);
  font-size: var(--xh-font-size-sm);
}
</style>
