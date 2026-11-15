<script setup lang="ts">
import type { CrmProspectingCandidateDto } from '@/api'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CrmProspectingCandidateStatus } from '@/api'
import { useProspectingLabels } from './prospecting-labels'

defineOptions({ name: 'CrmProspectingCandidateTable' })

const props = defineProps<{
  candidates: CrmProspectingCandidateDto[]
  selectedIds: string[]
  canExecute: boolean
  canCreateLead: boolean
  pageIndex: number
  totalPages: number
  total: number
}>()

const emit = defineEmits<{
  (event: 'toggle', id: string, checked: boolean): void
  (event: 'toggleAll', checked: boolean): void
  (event: 'view', candidate: CrmProspectingCandidateDto): void
  (event: 'review', candidate: CrmProspectingCandidateDto, approve: boolean): void
  (event: 'convert', candidate: CrmProspectingCandidateDto): void
  (event: 'page', pageIndex: number): void
}>()

const { t } = useI18n()
const { candidateStatus } = useProspectingLabels()

const allOnPageSelected = computed(() => props.candidates.length > 0
  && props.candidates.every(item => props.selectedIds.includes(String(item.basicId))))

function onToggle(id: string, event: Event) {
  emit('toggle', id, (event.target as HTMLInputElement).checked)
}

function onToggleAll(event: Event) {
  emit('toggleAll', (event.target as HTMLInputElement).checked)
}
</script>

<template>
  <div v-if="candidates.length === 0" class="empty-state">
    {{ t('crm.prospecting.noCandidates') }}
  </div>
  <table v-else>
    <thead><tr><th><input type="checkbox" :checked="allOnPageSelected" :aria-label="t('crm.prospecting.selectAll')" @change="onToggleAll"></th><th>{{ t('crm.prospecting.company') }}</th><th>{{ t('crm.prospecting.contact') }}</th><th>{{ t('crm.prospecting.email') }}</th><th>{{ t('crm.prospecting.website') }}</th><th>{{ t('crm.prospecting.source') }}</th><th>{{ t('crm.prospecting.status') }}</th><th>{{ t('crm.prospecting.duplicateReason') }}</th><th>{{ t('crm.prospecting.shortlistFilter') }}</th><th>{{ t('crm.prospecting.actions') }}</th></tr></thead>
    <tbody>
      <tr v-for="candidate in candidates" :key="candidate.basicId">
        <td><input type="checkbox" :checked="selectedIds.includes(String(candidate.basicId))" :aria-label="String(candidate.basicId)" @change="onToggle(String(candidate.basicId), $event)"></td>
        <td><strong>{{ candidate.companyName }}</strong><small>{{ candidate.title || '' }}</small></td>
        <td>{{ candidate.contactName || '—' }}</td><td>{{ candidate.businessEmail || '—' }}</td>
        <td><a v-if="candidate.website" :href="candidate.website" target="_blank" rel="noopener noreferrer">{{ candidate.website }}</a><span v-else>—</span></td>
        <td>{{ candidate.source || candidate.providerCode }}</td><td>{{ candidateStatus(candidate.status) }}</td><td>{{ candidate.duplicateReason || '—' }}</td><td>{{ candidate.shortlistedAt ? t('crm.prospecting.shortlisted') : '—' }}</td>
        <td class="actions">
          <button type="button" @click="emit('view', candidate)">
            {{ t('crm.prospecting.viewEvidence') }}
          </button>
          <template v-if="canExecute && [CrmProspectingCandidateStatus.PendingReview, CrmProspectingCandidateStatus.Duplicate].includes(candidate.status)">
            <button type="button" @click="emit('review', candidate, true)">
              {{ t('crm.prospecting.approve') }}
            </button>
            <button type="button" @click="emit('review', candidate, false)">
              {{ t('crm.prospecting.reject') }}
            </button>
          </template>
          <button v-if="canCreateLead && candidate.status === CrmProspectingCandidateStatus.Approved" type="button" @click="emit('convert', candidate)">
            {{ t('crm.prospecting.convertToLead') }}
          </button>
        </td>
      </tr>
    </tbody>
  </table>
  <nav v-if="total > 0" class="pagination-bar" :aria-label="t('crm.prospecting.candidatePagination')">
    <button type="button" :disabled="pageIndex <= 1" @click="emit('page', pageIndex - 1)">
      {{ t('crm.prospecting.prevPage') }}
    </button>
    <span class="page-info">
      {{ t('crm.prospecting.pageInfo', { page: pageIndex, pages: totalPages, total }) }}
    </span>
    <button type="button" :disabled="pageIndex >= totalPages" @click="emit('page', pageIndex + 1)">
      {{ t('crm.prospecting.nextPage') }}
    </button>
  </nav>
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
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 0.75rem;
  font-size: 0.9rem;
}
th,
td {
  padding: 0.65rem 0.5rem;
  border-bottom: 1px solid var(--xh-border, #ddd);
  text-align: left;
  vertical-align: top;
}
td small {
  display: block;
  color: var(--xh-text-muted, #777);
}
.empty-state {
  padding: 1.5rem;
  text-align: center;
  color: var(--xh-text-muted, #777);
}
.pagination-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 0.75rem;
}
.pagination-bar .page-info {
  color: var(--xh-fg-muted);
  font-size: 0.9rem;
}
</style>
