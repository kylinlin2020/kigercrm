<script setup lang="ts">
import type { CrmProspectingRunDto, CrmProspectingRunMonitorDto } from '@/api'
import { useI18n } from 'vue-i18n'

defineOptions({ name: 'CrmProspectingRunMonitorPanel' })

defineProps<{
  /** Selected run: counters row stays with the freshest GET /CrmProspecting/Run payload. */
  run: CrmProspectingRunDto
  /** Progress aggregate + per-provider ledger; null until the first monitor read lands. */
  monitor: CrmProspectingRunMonitorDto | null
  /** True while the page is polling this run (drives the auto-refresh badge). */
  polling: boolean
}>()

const { t } = useI18n()

function runStatus(value: number) {
  return [
    t('crm.prospecting.queued'),
    t('crm.prospecting.running'),
    t('crm.prospecting.succeeded'),
    t('crm.prospecting.partiallySucceeded'),
    t('crm.prospecting.failed'),
    t('crm.prospecting.cancelRequested'),
    t('crm.prospecting.cancelled'),
    t('crm.prospecting.waitingForExtractions'),
  ][value] ?? String(value)
}

const EXECUTION_STATE_KEYS: Record<string, string> = {
  Queued: 'crm.prospecting.execStateQueued',
  Started: 'crm.prospecting.execStateStarted',
  Checkpointed: 'crm.prospecting.execStateCheckpointed',
  Projecting: 'crm.prospecting.execStateProjecting',
  Succeeded: 'crm.prospecting.execStateSucceeded',
  Failed: 'crm.prospecting.execStateFailed',
  NeedsReview: 'crm.prospecting.execStateNeedsReview',
  Skipped: 'crm.prospecting.execStateSkipped',
}

function executionState(state: string) {
  const key = EXECUTION_STATE_KEYS[state]
  // Unknown states fall back to the raw ledger value instead of claiming a known meaning.
  return key ? t(key) : state
}

function formatTime(value?: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}

function formatFailure(item: CrmProspectingRunMonitorDto['executions'][number]) {
  if (!item.failureCode && !item.failureSummary)
    return '—'
  return [item.failureCode, item.failureSummary].filter(Boolean).join(': ')
}
</script>

<template>
  <div class="run-monitor">
    <div class="run-detail">
      <span>{{ t('crm.prospecting.status') }}: {{ runStatus(run.status) }}</span>
      <span>{{ t('crm.prospecting.rawCount') }}: {{ run.rawRecordCount }}</span>
      <span>{{ t('crm.prospecting.candidateCount') }}: {{ run.candidateCount }}</span>
      <span>{{ t('crm.prospecting.failures') }}: {{ run.failureCount }}</span>
      <span v-if="run.failureSummary">{{ t('crm.prospecting.failureSummary') }}: {{ run.failureSummary }}</span>
      <span v-if="polling" class="live-badge">{{ t('crm.prospecting.autoRefresh') }}</span>
    </div>

    <template v-if="monitor">
      <div class="progress-block">
        <div
          class="progress-track"
          role="progressbar"
          :aria-label="t('crm.prospecting.progressSummary', { percent: monitor.progress.percent, completed: monitor.progress.completedSteps, total: monitor.progress.totalSteps })"
          :aria-valuenow="monitor.progress.percent"
          aria-valuemin="0"
          aria-valuemax="100"
        >
          <div class="progress-fill" :style="{ width: `${monitor.progress.percent}%` }" />
        </div>
        <div class="progress-summary">
          <span class="progress-text">{{ t('crm.prospecting.progressSummary', { percent: monitor.progress.percent, completed: monitor.progress.completedSteps, total: monitor.progress.totalSteps }) }}</span>
          <span v-if="monitor.progress.succeededSteps > 0" class="step-chip chip-ok">{{ t('crm.prospecting.stepSucceeded') }} {{ monitor.progress.succeededSteps }}</span>
          <span v-if="monitor.progress.runningSteps > 0" class="step-chip">{{ t('crm.prospecting.stepRunning') }} {{ monitor.progress.runningSteps }}</span>
          <span v-if="monitor.progress.queuedSteps > 0" class="step-chip">{{ t('crm.prospecting.stepQueued') }} {{ monitor.progress.queuedSteps }}</span>
          <span v-if="monitor.progress.failedSteps > 0" class="step-chip chip-alert">{{ t('crm.prospecting.stepFailed') }} {{ monitor.progress.failedSteps }}</span>
          <span v-if="monitor.progress.needsReviewSteps > 0" class="step-chip chip-warn">{{ t('crm.prospecting.stepNeedsReview') }} {{ monitor.progress.needsReviewSteps }}</span>
          <span v-if="monitor.progress.skippedSteps > 0" class="step-chip">{{ t('crm.prospecting.stepSkipped') }} {{ monitor.progress.skippedSteps }}</span>
        </div>
      </div>

      <h3 class="exec-title">
        {{ t('crm.prospecting.providerExecutions') }}
      </h3>
      <div v-if="monitor.executions.length === 0" class="empty-state">
        {{ t('crm.prospecting.noExecutions') }}
      </div>
      <div v-else class="exec-scroll">
        <table class="exec-table">
          <thead>
            <tr>
              <th>{{ t('crm.prospecting.provider') }}</th>
              <th>{{ t('crm.prospecting.status') }}</th>
              <th>{{ t('crm.prospecting.execAttempts') }}</th>
              <th>{{ t('crm.prospecting.rawCount') }}</th>
              <th>{{ t('crm.prospecting.execProjected') }}</th>
              <th>{{ t('crm.prospecting.failures') }}</th>
              <th>{{ t('crm.prospecting.execWindow') }}</th>
              <th>{{ t('crm.prospecting.execFailure') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in monitor.executions" :key="item.basicId">
              <td><strong><code>{{ item.providerCode }}</code></strong><small>#{{ item.planOrdinal }} · v{{ item.planVersion }}</small></td>
              <td>
                <span
                  class="state-chip"
                  :class="{
                    'state-ok': item.state === 'Succeeded',
                    'state-alert': item.state === 'Failed' || item.state === 'NeedsReview',
                  }"
                >{{ executionState(item.state) }}</span>
              </td>
              <td>{{ item.attemptCount }}</td>
              <td>{{ item.rawRecordCount }}</td>
              <td>{{ item.projectedRecordCount }}</td>
              <td>{{ item.failureCount }}</td>
              <td class="window-cell">
                {{ formatTime(item.startedAt) }} → {{ formatTime(item.completedAt) }}
              </td>
              <td class="failure-cell">
                {{ formatFailure(item) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<style scoped>
.run-monitor {
  display: grid;
  gap: 0.75rem;
}
.run-detail {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  padding: 0.55rem 0.75rem;
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
  font-size: 0.85rem;
}
.live-badge {
  margin-left: auto;
  padding: 0.2rem 0.6rem;
  border-radius: var(--radius-full);
  border: 1px solid var(--xh-border-default, #ddd);
  color: var(--xh-fg-brand, #2563eb);
  font-size: 0.8rem;
  white-space: nowrap;
}
.progress-block {
  display: grid;
  gap: 0.5rem;
}
.progress-track {
  height: 0.5rem;
  border-radius: var(--radius-full);
  background: var(--xh-border-subtle, #eee);
  overflow: hidden;
}
.progress-fill {
  height: 100%;
  border-radius: var(--radius-full);
  background: var(--xh-fg-brand, #2563eb);
  transition: width var(--xh-motion-duration-move, 200ms) var(--xh-motion-ease-exit, ease);
}
.progress-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
}
.progress-text {
  font-weight: 600;
}
.step-chip,
.state-chip {
  padding: 0.2rem 0.6rem;
  border-radius: var(--radius-full);
  background: var(--xh-bg-subtle, #eee);
  font-size: 0.8rem;
  white-space: nowrap;
}
.chip-ok,
.state-ok {
  background: color-mix(in srgb, var(--xh-fg-success, #16a34a) 14%, transparent);
  color: var(--xh-fg-success, #16a34a);
}
.chip-warn {
  background: color-mix(in srgb, var(--xh-fg-warning, #b45309) 14%, transparent);
  color: var(--xh-fg-warning, #b45309);
}
.chip-alert,
.state-alert {
  background: color-mix(in srgb, var(--xh-fg-danger, #dc2626) 14%, transparent);
  color: var(--xh-fg-danger, #dc2626);
}
.exec-title {
  margin: 0.25rem 0 0;
  font-size: 0.95rem;
}
.exec-scroll {
  overflow-x: auto;
}
.exec-table {
  width: 100%;
  min-width: 52rem;
  border-collapse: collapse;
  font-size: 0.85rem;
}
.exec-table th,
.exec-table td {
  padding: 0.55rem 0.5rem;
  border-bottom: 1px solid var(--xh-border-subtle, #ddd);
  text-align: left;
  vertical-align: top;
}
.exec-table td small {
  display: block;
  color: var(--xh-fg-muted, #777);
}
.exec-table code {
  font-size: 0.8rem;
}
.window-cell {
  white-space: nowrap;
}
.failure-cell {
  max-width: 18rem;
  overflow-wrap: anywhere;
  color: var(--xh-fg-muted, #777);
}
.empty-state {
  padding: 1.25rem;
  text-align: center;
  color: var(--xh-fg-muted, #777);
}
@media (prefers-reduced-motion: reduce) {
  .progress-fill {
    transition: none;
  }
}
</style>
