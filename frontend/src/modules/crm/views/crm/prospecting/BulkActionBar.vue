<script setup lang="ts">
import type { CrmProspectingBulkAction } from './view-types'
import type { CrmProspectingBulkResultDto } from '@/api'
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
    <button v-if="canExecute" type="button" :disabled="busy" @click="emit('action', 'approve')">
      {{ t('crm.prospecting.bulkApprove') }}
    </button>
    <button v-if="canExecute" type="button" :disabled="busy" @click="emit('action', 'reject')">
      {{ t('crm.prospecting.bulkReject') }}
    </button>
    <button v-if="canCreateLead" type="button" :disabled="busy" @click="emit('action', 'convert')">
      {{ t('crm.prospecting.bulkConvert') }}
    </button>
    <button v-if="canExecute" type="button" :disabled="busy" @click="emit('action', 'shortlist')">
      {{ t('crm.prospecting.bulkShortlist') }}
    </button>
    <button v-if="canExecute" type="button" :disabled="busy" @click="emit('action', 'unshortlist')">
      {{ t('crm.prospecting.bulkUnshortlist') }}
    </button>
    <button type="button" @click="emit('clear')">
      {{ t('crm.prospecting.bulkClearSelection') }}
    </button>
  </div>
  <div v-if="result" class="bulk-result">
    <div class="bulk-result-header">
      <h3>{{ t('crm.prospecting.bulkResultTitle') }}</h3>
      <button type="button" @click="emit('dismiss')">
        {{ t('crm.prospecting.closeDetail') }}
      </button>
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
button {
  border: 1px solid var(--xh-border, #ccc);
  border-radius: 0.4rem;
  padding: 0.45rem 0.7rem;
}
button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
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
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-muted-surface, #f5f5f5);
}
.bulk-result {
  padding: 0.5rem 0.75rem;
  margin-bottom: 0.75rem;
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.5rem;
}
.bulk-failures {
  margin: 0.5rem 0 0;
  padding-left: 1.25rem;
  font-size: 0.85rem;
  color: var(--xh-text-muted, #777);
}
</style>
