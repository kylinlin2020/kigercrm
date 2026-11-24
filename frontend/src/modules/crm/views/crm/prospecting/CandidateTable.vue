<script setup lang="ts">
import type { CrmProspectingCandidateDto } from '@/api'
import { XhButton, XhButtonLabel, XhCheckbox } from '@xihan-ui/vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CrmProspectingCandidateStatus } from '@/api'
import { safeExternalUrl } from '../utils/safe-external-url'
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

function onToggle(id: string, checked: boolean) {
  emit('toggle', id, checked)
}

function onToggleAll(checked: boolean) {
  emit('toggleAll', checked)
}
</script>

<template>
  <div v-if="candidates.length === 0" class="empty-state">
    {{ t('crm.prospecting.noCandidates') }}
  </div>
  <table v-else>
    <thead><tr><th><XhCheckbox :checked="allOnPageSelected" :aria-label="t('crm.prospecting.selectAll')" @update:checked="onToggleAll" /></th><th>{{ t('crm.prospecting.company') }}</th><th>{{ t('crm.prospecting.contact') }}</th><th>{{ t('crm.prospecting.email') }}</th><th>{{ t('crm.prospecting.website') }}</th><th>{{ t('crm.prospecting.source') }}</th><th>{{ t('crm.prospecting.status') }}</th><th>{{ t('crm.prospecting.duplicateReason') }}</th><th>{{ t('crm.prospecting.shortlistFilter') }}</th><th>{{ t('crm.prospecting.actions') }}</th></tr></thead>
    <tbody>
      <tr v-for="candidate in candidates" :key="candidate.basicId">
        <td><XhCheckbox :checked="selectedIds.includes(String(candidate.basicId))" :aria-label="String(candidate.basicId)" @update:checked="onToggle(String(candidate.basicId), $event)" /></td>
        <td><strong>{{ candidate.companyName }}</strong><small>{{ candidate.title || '' }}</small></td>
        <td>{{ candidate.contactName || '—' }}</td><td>{{ candidate.businessEmail || '—' }}</td>
        <td><a v-if="safeExternalUrl(candidate.website)" :href="safeExternalUrl(candidate.website) ?? undefined" target="_blank" rel="noopener noreferrer">{{ candidate.website }}</a><span v-else>—</span></td>
        <td>{{ candidate.source || candidate.providerCode }}</td><td>{{ candidateStatus(candidate.status) }}</td><td>{{ candidate.duplicateReason || '—' }}</td><td>{{ candidate.shortlistedAt ? t('crm.prospecting.shortlisted') : '—' }}</td>
        <td class="actions">
          <XhButton variant="outline" @click="emit('view', candidate)">
            <XhButtonLabel>{{ t('crm.prospecting.viewEvidence') }}</XhButtonLabel>
          </XhButton>
          <template v-if="canExecute && [CrmProspectingCandidateStatus.PendingReview, CrmProspectingCandidateStatus.Duplicate].includes(candidate.status)">
            <XhButton variant="outline" @click="emit('review', candidate, true)">
              <XhButtonLabel>{{ t('crm.prospecting.approve') }}</XhButtonLabel>
            </XhButton>
            <XhButton variant="outline" tone="danger" @click="emit('review', candidate, false)">
              <XhButtonLabel>{{ t('crm.prospecting.reject') }}</XhButtonLabel>
            </XhButton>
          </template>
          <XhButton v-if="canCreateLead && candidate.status === CrmProspectingCandidateStatus.Approved" variant="outline" @click="emit('convert', candidate)">
            <XhButtonLabel>{{ t('crm.prospecting.convertToLead') }}</XhButtonLabel>
          </XhButton>
        </td>
      </tr>
    </tbody>
  </table>
  <nav v-if="total > 0" class="pagination-bar" :aria-label="t('crm.prospecting.candidatePagination')">
    <XhButton variant="subtle" :disabled="pageIndex <= 1" @click="emit('page', pageIndex - 1)">
      <XhButtonLabel>{{ t('crm.prospecting.prevPage') }}</XhButtonLabel>
    </XhButton>
    <span class="page-info">
      {{ t('crm.prospecting.pageInfo', { page: pageIndex, pages: totalPages, total }) }}
    </span>
    <XhButton variant="subtle" :disabled="pageIndex >= totalPages" @click="emit('page', pageIndex + 1)">
      <XhButtonLabel>{{ t('crm.prospecting.nextPage') }}</XhButtonLabel>
    </XhButton>
  </nav>
</template>

<style scoped>
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
  border-bottom: 1px solid hsl(var(--border));
  text-align: left;
  vertical-align: top;
}
td small {
  display: block;
  color: hsl(var(--muted-foreground));
}
.empty-state {
  padding: 1.5rem;
  text-align: center;
  color: hsl(var(--muted-foreground));
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
  color: hsl(var(--muted-foreground));
  font-size: 0.9rem;
}
</style>
