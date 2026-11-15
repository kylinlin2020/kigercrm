<script setup lang="ts">
import type { CrmProspectingCandidateReviewDetailDto } from '@/api'
import { useI18n } from 'vue-i18n'

defineOptions({ name: 'CrmProspectingReviewDetailPanel' })

defineProps<{
  loading: boolean
  detail: CrmProspectingCandidateReviewDetailDto | null
}>()

const emit = defineEmits<{
  (event: 'close'): void
}>()

const { t } = useI18n()

/** Only absolute HTTPS URLs without embedded credentials are rendered as links. */
function safeEvidenceUrl(value: string) {
  try {
    const url = new URL(value)
    if (url.protocol !== 'https:' || url.username || url.password)
      return null
    return url.href
  }
  catch {
    return null
  }
}

function scorePriority(value: number) {
  return [t('crm.prospecting.priorityHold'), 'C', 'B', 'A'][value] ?? String(value)
}

function scoreGates(value: string) {
  try {
    const parsed = JSON.parse(value)
    return Array.isArray(parsed) ? parsed.join(', ') : value
  }
  catch {
    return value
  }
}
</script>

<template>
  <section v-if="loading || detail" class="panel review-detail">
    <header class="section-header">
      <div><h2>{{ t('crm.prospecting.reviewDetail') }}</h2><p>{{ detail?.candidate.companyName || '' }}</p></div>
      <button type="button" @click="emit('close')">
        {{ t('crm.prospecting.closeDetail') }}
      </button>
    </header>
    <div v-if="loading" class="empty-state">
      {{ t('crm.prospecting.loadingDetails') }}
    </div>
    <template v-else-if="detail">
      <h3>{{ t('crm.prospecting.scoreHistory') }}</h3>
      <div v-if="detail.scoreHistory.length === 0" class="empty-state">
        {{ t('crm.prospecting.noScore') }}
      </div>
      <table v-else>
        <thead><tr><th>{{ t('crm.prospecting.totalScore') }}</th><th>{{ t('crm.prospecting.priority') }}</th><th>{{ t('crm.prospecting.scoreVersion') }}</th><th>{{ t('crm.prospecting.scoreBreakdown') }}</th><th>{{ t('crm.prospecting.reviewGates') }}</th><th>{{ t('crm.prospecting.evaluatedAt') }}</th></tr></thead>
        <tbody>
          <tr v-for="score in detail.scoreHistory" :key="score.basicId">
            <td><strong>{{ score.total }}/100</strong></td><td>{{ scorePriority(score.priority) }}</td><td>{{ score.scorecardVersion }}</td>
            <td>{{ score.identityPoints }}/15 · {{ score.procurementPoints }}/30 · {{ score.productFitPoints }}/20 · {{ score.marketFitPoints }}/10 · {{ score.recencyPoints }}/10 · {{ score.evidencePoints }}/10 · {{ score.contactPoints }}/5</td>
            <td>{{ scoreGates(score.reviewGatesJson) || '—' }}</td><td>{{ new Date(score.evaluatedAtUtc).toLocaleString() }}</td>
          </tr>
        </tbody>
      </table>
      <h3>{{ t('crm.prospecting.evidence') }}</h3>
      <div v-if="detail.evidence.length === 0" class="empty-state">
        {{ t('crm.prospecting.noEvidence') }}
      </div>
      <table v-else>
        <thead><tr><th>{{ t('crm.prospecting.field') }}</th><th>{{ t('crm.prospecting.provider') }}</th><th>{{ t('crm.prospecting.verification') }}</th><th>{{ t('crm.prospecting.evidenceType') }}</th><th>{{ t('crm.prospecting.sourceUrl') }}</th><th>{{ t('crm.prospecting.retrievedAt') }}</th></tr></thead>
        <tbody>
          <tr v-for="item in detail.evidence" :key="item.basicId">
            <td><strong>{{ item.fieldName }}</strong><small>{{ item.evidenceSummary || item.fieldValueHash || '' }}</small></td><td>{{ item.providerCode }}<small>{{ item.providerRecordId || '' }}</small></td><td>{{ item.verificationStatus }}</td><td>{{ item.evidenceType }}</td>
            <td><a v-if="safeEvidenceUrl(item.sourceUrl)" :href="item.sourceUrl" target="_blank" rel="noopener noreferrer">{{ item.sourceUrl }}</a><span v-else>{{ item.sourceUrl }}</span></td><td>{{ new Date(item.retrievedAtUtc).toLocaleString() }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </section>
</template>

<style scoped>
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}
.section-header h2 {
  margin: 0;
}
.section-header p {
  color: var(--xh-text-muted, #777);
  margin: 0.25rem 0 0;
}
.panel {
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.75rem;
  padding: 1rem;
  overflow-x: auto;
}
button {
  border: 1px solid var(--xh-border, #ccc);
  border-radius: 0.4rem;
  padding: 0.45rem 0.7rem;
}
.review-detail {
  display: grid;
  gap: 0.75rem;
}
.review-detail h3 {
  margin: 0.5rem 0 0;
}
.review-detail td small {
  display: block;
  max-width: 22rem;
  overflow-wrap: anywhere;
  color: var(--xh-text-muted, #777);
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
@media (max-width: 760px) {
  .section-header {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>
