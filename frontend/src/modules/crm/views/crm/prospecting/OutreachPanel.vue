<script setup lang="ts">
import type {
  CrmOutreachEnrollmentDto,
  CrmOutreachReviewDto,
  CrmOutreachSequenceDto,
  CrmOutreachTouchpointDto,
} from '@/api'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  CrmOutreachEnrollmentStatus,
  CrmOutreachSequenceStatus,
  CrmOutreachTouchpointState,
  crmProspectingApi,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingOutreachPanel' })

const { t } = useI18n()
const { hasPermission } = usePermission()
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))

const sequences = ref<CrmOutreachSequenceDto[]>([])
const sequenceId = ref('')
const name = ref('')
const stepsJson = ref('[{"channel":0,"delayDays":0,"template":"introduction"},{"channel":3,"delayDays":3,"template":"follow-up"}]')
const candidateId = ref('')
const enrollments = ref<CrmOutreachEnrollmentDto[]>([])
const touchpoints = ref<CrmOutreachTouchpointDto[]>([])
const review = ref<CrmOutreachReviewDto | null>(null)
const replyText = ref('')
const busy = ref(false)

async function loadSequences() {
  try {
    sequences.value = await crmProspectingApi.outreachSequences()
    if (!sequences.value.some(item => String(item.basicId) === sequenceId.value)) {
      sequenceId.value = sequences.value[0] ? String(sequences.value[0].basicId) : ''
    }
    await loadWorkspace()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.outreachError'))
  }
}

async function loadWorkspace() {
  if (!sequenceId.value) {
    enrollments.value = []
    touchpoints.value = []
    review.value = null
    return
  }
  try {
    const [rows, summary] = await Promise.all([
      crmProspectingApi.outreachEnrollments(sequenceId.value),
      crmProspectingApi.outreachReview(sequenceId.value),
    ])
    enrollments.value = rows
    review.value = summary
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.outreachError'))
  }
}

onMounted(loadSequences)

async function act(task: () => Promise<unknown>) {
  if (!canExecute.value || busy.value) {
    return
  }
  busy.value = true
  try {
    await task()
    await loadSequences()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.outreachError'))
  }
  finally {
    busy.value = false
  }
}

function createSequence() {
  let steps: unknown
  try {
    steps = JSON.parse(stepsJson.value)
  }
  catch {
    toast.danger(t('crm.prospecting.outreachInvalidSteps'))
    return
  }
  void act(() => crmProspectingApi.createOutreachSequence({
    name: name.value,
    steps: steps as typeof sequences.value[number]['steps'],
  }))
}

async function openEnrollment(enrollmentId: string) {
  try {
    touchpoints.value = await crmProspectingApi.outreachTouchpoints(enrollmentId)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.outreachError'))
  }
}

function enrollmentLabel(value: CrmOutreachEnrollmentStatus) {
  return [
    t('crm.prospecting.outreachEnrollActive'),
    t('crm.prospecting.outreachEnrollPaused'),
    t('crm.prospecting.outreachEnrollCompleted'),
    t('crm.prospecting.outreachEnrollSuppressed'),
  ][value] ?? String(value)
}

function stateLabel(value: CrmOutreachTouchpointState) {
  return [
    t('crm.prospecting.outreachPlanned'),
    t('crm.prospecting.outreachApproved'),
    t('crm.prospecting.outreachSent'),
    t('crm.prospecting.outreachReplied'),
    t('crm.prospecting.outreachBounced'),
    t('crm.prospecting.outreachSuppressed'),
  ][value] ?? String(value)
}
</script>

<template>
  <section class="outreach-panel">
    <header class="section-header">
      <div>
        <h2>{{ t('crm.prospecting.outreachTitle') }}</h2>
        <p>{{ t('crm.prospecting.outreachDescription') }}</p>
      </div>
      <label>{{ t('crm.prospecting.outreachSequence') }}
        <select v-model="sequenceId" @change="loadWorkspace">
          <option value="">{{ t('crm.prospecting.outreachSelectSequence') }}</option>
          <option v-for="sequence in sequences" :key="sequence.basicId" :value="String(sequence.basicId)">
            {{ sequence.name }}
          </option>
        </select>
      </label>
    </header>

    <div class="outreach-form">
      <label>{{ t('crm.prospecting.outreachName') }}
        <input v-model="name" type="text" maxlength="160">
      </label>
      <label>{{ t('crm.prospecting.outreachSteps') }}
        <textarea v-model="stepsJson" rows="3" />
      </label>
      <button v-if="canExecute" type="button" :disabled="busy" @click="createSequence">
        {{ t('crm.prospecting.outreachCreateSequence') }}
      </button>
    </div>

    <div v-for="sequence in sequences" :key="sequence.basicId" class="outreach-row">
      <strong>{{ sequence.name }}</strong>
      <span>{{ t('crm.prospecting.outreachStepCount', { count: sequence.steps.length }) }}</span>
      <button
        v-if="canExecute && sequence.status === CrmOutreachSequenceStatus.Draft"
        type="button"
        @click="act(() => crmProspectingApi.activateOutreachSequence(sequence.basicId))"
      >
        {{ t('crm.prospecting.outreachActivate') }}
      </button>
      <button
        v-if="canExecute && sequence.status === CrmOutreachSequenceStatus.Active"
        type="button"
        @click="act(() => crmProspectingApi.pauseOutreachSequence(sequence.basicId))"
      >
        {{ t('crm.prospecting.outreachPause') }}
      </button>
    </div>

    <div class="outreach-form">
      <label>{{ t('crm.prospecting.outreachCandidateId') }}
        <input v-model="candidateId" type="text">
      </label>
      <button
        v-if="canExecute"
        type="button"
        :disabled="busy || !sequenceId || !candidateId"
        @click="act(() => crmProspectingApi.enrollForOutreach({ sequenceId, candidateId }))"
      >
        {{ t('crm.prospecting.outreachEnroll') }}
      </button>
    </div>

    <div v-if="review" class="outreach-metrics">
      {{ t('crm.prospecting.outreachMetrics', {
        enrollments: review.enrollments,
        approved: review.approved,
        sent: review.sent,
        replied: review.replied,
        suppressed: review.suppressed,
      }) }}
    </div>

    <div v-for="enrollment in enrollments" :key="enrollment.basicId" class="outreach-row">
      <div class="outreach-main">
        <strong>#{{ enrollment.candidateId }}</strong>
        <small>{{ enrollmentLabel(enrollment.status) }} · {{ t('crm.prospecting.outreachStep', { step: enrollment.currentStep }) }}</small>
      </div>
      <button type="button" @click="openEnrollment(enrollment.basicId)">
        {{ t('crm.prospecting.outreachTouchpoints') }}
      </button>
      <button
        v-if="canExecute && enrollment.status !== CrmOutreachEnrollmentStatus.Suppressed"
        type="button"
        @click="act(() => crmProspectingApi.suppressOutreachEnrollment(enrollment.basicId, 'operator opt-out'))"
      >
        {{ t('crm.prospecting.outreachSuppress') }}
      </button>
    </div>

    <div v-for="touchpoint in touchpoints" :key="touchpoint.basicId" class="outreach-row">
      <div class="outreach-main">
        <strong>{{ t('crm.prospecting.outreachStep', { step: touchpoint.step }) }}</strong>
        <small>{{ stateLabel(touchpoint.state) }} · {{ touchpoint.contentHash.slice(0, 12) }}</small>
      </div>
      <button
        v-if="canExecute && touchpoint.state === CrmOutreachTouchpointState.Planned"
        type="button"
        @click="act(() => crmProspectingApi.approveOutreachTouchpoint(touchpoint.basicId))"
      >
        {{ t('crm.prospecting.outreachApprove') }}
      </button>
      <template v-if="canExecute && touchpoint.state === CrmOutreachTouchpointState.Approved">
        <input v-model="replyText" type="text" :placeholder="t('crm.prospecting.outreachReplyPlaceholder')">
        <button type="button" @click="act(() => crmProspectingApi.recordOutreachTouchpoint({ touchpointId: touchpoint.basicId, state: CrmOutreachTouchpointState.Sent }))">
          {{ t('crm.prospecting.outreachMarkSent') }}
        </button>
        <button type="button" @click="act(() => crmProspectingApi.recordOutreachTouchpoint({ touchpointId: touchpoint.basicId, state: CrmOutreachTouchpointState.Replied, replyText }))">
          {{ t('crm.prospecting.outreachMarkReplied') }}
        </button>
      </template>
    </div>
  </section>
</template>

<style scoped>
.outreach-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.outreach-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.outreach-form label {
  display: grid;
  flex: 1 1 14rem;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.outreach-form input,
.outreach-form textarea,
.section-header select {
  width: 100%;
  font: inherit;
}
.outreach-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.outreach-main {
  display: grid;
  gap: 0.2rem;
}
.outreach-main small {
  color: var(--xh-fg-muted, #777);
}
.outreach-metrics {
  font-size: 0.8rem;
  color: var(--xh-fg-muted, #777);
}
</style>
