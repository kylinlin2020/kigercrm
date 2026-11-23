<script setup lang="ts">
import type { CrmProspectingBulkAction } from './view-types'
import type { CrmProspectingBulkResultDto } from '@/api'
import { XhButton, XhButtonLabel } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CrmProspectingBulkOutcome } from '@/api'

defineOptions({ name: 'CrmProspectingBulkActionBar' })

const props = defineProps<{
  /** IDs selected on the current page; kept in the parent so Run switches can clear it. */
  selectedCount: number
  busy: boolean
  /** Mirrors the backend Execute/Create permission attributes; UI-only, never a security boundary. */
  canExecute: boolean
  canCreateLead: boolean
  result: CrmProspectingBulkResultDto | null
}>()

const emit = defineEmits<{
  (event: 'action', value: CrmProspectingBulkAction): void
  (event: 'clear'): void
  (event: 'dismiss'): void
}>()

const { t } = useI18n()

const success = computed(() => props.result?.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Success).length ?? 0)
const skipped = computed(() => props.result?.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Skipped).length ?? 0)
const failed = computed(() => props.result?.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Failed).length ?? 0)
const failures = computed(() => props.result?.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Failed) ?? [])
</script>

<template>
  <div v-if="selectedCount > 0" class="bulk-action-bar">
    <strong>{{ t('crm.prospecting.selectedCount', { count: selectedCount }) }}</strong>
    <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="emit('action', 'approve')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkApprove') }}</XhButtonLabel>
    </XhButton>
    <XhButton v-if="canExecute" variant="outline" tone="danger" :disabled="busy" @click="emit('action', 'reject')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkReject') }}</XhButtonLabel>
    </XhButton>
    <XhButton v-if="canCreateLead" variant="outline" :disabled="busy" @click="emit('action', 'convert')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkConvert') }}</XhButtonLabel>
    </XhButton>
    <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="emit('action', 'shortlist')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkShortlist') }}</XhButtonLabel>
    </XhButton>
    <XhButton v-if="canExecute" variant="subtle" :disabled="busy" @click="emit('action', 'unshortlist')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkUnshortlist') }}</XhButtonLabel>
    </XhButton>
    <XhButton variant="subtle" @click="emit('clear')">
      <XhButtonLabel>{{ t('crm.prospecting.bulkClearSelection') }}</XhButtonLabel>
    </XhButton>
  </div>
  <div v-if="result" class="bulk-result">
    <div class="bulk-result-header">
      <h3>{{ t('crm.prospecting.bulkResultTitle') }}</h3>
      <XhButton variant="subtle" @click="emit('dismiss')">
        <XhButtonLabel>{{ t('crm.prospecting.closeDetail') }}</XhButtonLabel>
      </XhButton>
    </div>
    <p>{{ t('crm.prospecting.bulkResultSummary', { success, skipped, failed }) }}</p>
    <ul v-if="failures.length" class="bulk-failures">
      <li v-for="item in failures" :key="item.candidateId">
        <code>{{ item.candidateId }}</code> · {{ item.reason }}
      </li>
    </ul>
  </div>
</template>

<style scoped>
.bulk-result-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}
.bulk-result-header h3 {
  margin: 0;
}
.bulk-action-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding: 0.6rem 0.75rem;
  margin-bottom: 0.75rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.bulk-result {
  padding: 0.5rem 0.75rem;
  margin-bottom: 0.75rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
}
.bulk-failures {
  margin: 0.5rem 0 0;
  padding-left: 1.25rem;
  font-size: 0.85rem;
  color: hsl(var(--muted-foreground));
}
</style>
