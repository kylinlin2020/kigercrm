<script setup lang="ts">
import type {
  CrmOutreachEnrollmentDto,
  CrmOutreachReviewDto,
  CrmOutreachSequenceDto,
  CrmOutreachTouchpointDto,
} from '@/api'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  CrmOutreachEnrollmentStatus,
  CrmOutreachSequenceStatus,
  CrmOutreachTouchpointState,
  crmProspectingApi,
} from '@/api'
import { XInput, XSelect } from '~/components'
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
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.outreachSequence') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect
            v-model:value="sequenceId"
            class="outreach-select"
            :options="[
              { label: t('crm.prospecting.outreachSelectSequence'), value: '' },
              ...sequences.map(sequence => ({ label: sequence.name, value: String(sequence.basicId) })),
            ]"
            @update:value="loadWorkspace"
          />
        </XhFieldControl>
      </XhFieldRoot>
    </header>

    <div class="outreach-form">
      <XhFieldRoot class="outreach-field">
        <XhFieldLabel>{{ t('crm.prospecting.outreachName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="name" type="text" :max-length="160" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="outreach-field">
        <XhFieldLabel>{{ t('crm.prospecting.outreachSteps') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="stepsJson" type="textarea" :rows="3" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="createSequence">
        <XhButtonLabel>{{ t('crm.prospecting.outreachCreateSequence') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div v-for="sequence in sequences" :key="sequence.basicId" class="outreach-row">
      <strong>{{ sequence.name }}</strong>
      <span>{{ t('crm.prospecting.outreachStepCount', { count: sequence.steps.length }) }}</span>
      <XhButton
        v-if="canExecute && sequence.status === CrmOutreachSequenceStatus.Draft"
        variant="outline"
        @click="act(() => crmProspectingApi.activateOutreachSequence(sequence.basicId))"
      >
        <XhButtonLabel>{{ t('crm.prospecting.outreachActivate') }}</XhButtonLabel>
      </XhButton>
      <XhButton
        v-if="canExecute && sequence.status === CrmOutreachSequenceStatus.Active"
        variant="subtle"
        @click="act(() => crmProspectingApi.pauseOutreachSequence(sequence.basicId))"
      >
        <XhButtonLabel>{{ t('crm.prospecting.outreachPause') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div class="outreach-form">
      <XhFieldRoot class="outreach-field">
        <XhFieldLabel>{{ t('crm.prospecting.outreachCandidateId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="candidateId" type="text" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton
        v-if="canExecute"
        variant="outline"
        :disabled="busy || !sequenceId || !candidateId"
        @click="act(() => crmProspectingApi.enrollForOutreach({ sequenceId, candidateId }))"
      >
        <XhButtonLabel>{{ t('crm.prospecting.outreachEnroll') }}</XhButtonLabel>
      </XhButton>
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
      <XhButton variant="subtle" @click="openEnrollment(enrollment.basicId)">
        <XhButtonLabel>{{ t('crm.prospecting.outreachTouchpoints') }}</XhButtonLabel>
      </XhButton>
      <XhButton
        v-if="canExecute && enrollment.status !== CrmOutreachEnrollmentStatus.Suppressed"
        variant="subtle"
        tone="danger"
        @click="act(() => crmProspectingApi.suppressOutreachEnrollment(enrollment.basicId, 'operator opt-out'))"
      >
        <XhButtonLabel>{{ t('crm.prospecting.outreachSuppress') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div v-for="touchpoint in touchpoints" :key="touchpoint.basicId" class="outreach-row">
      <div class="outreach-main">
        <strong>{{ t('crm.prospecting.outreachStep', { step: touchpoint.step }) }}</strong>
        <small>{{ stateLabel(touchpoint.state) }} · {{ touchpoint.contentHash.slice(0, 12) }}</small>
      </div>
      <XhButton
        v-if="canExecute && touchpoint.state === CrmOutreachTouchpointState.Planned"
        variant="outline"
        @click="act(() => crmProspectingApi.approveOutreachTouchpoint(touchpoint.basicId))"
      >
        <XhButtonLabel>{{ t('crm.prospecting.outreachApprove') }}</XhButtonLabel>
      </XhButton>
      <template v-if="canExecute && touchpoint.state === CrmOutreachTouchpointState.Approved">
        <XhFieldRoot class="outreach-reply-field">
          <XhFieldLabel class="sr-only">
            {{ t('crm.prospecting.outreachReplyPlaceholder') }}
          </XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="replyText" type="text" :placeholder="t('crm.prospecting.outreachReplyPlaceholder')" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="outline" @click="act(() => crmProspectingApi.recordOutreachTouchpoint({ touchpointId: touchpoint.basicId, state: CrmOutreachTouchpointState.Sent }))">
          <XhButtonLabel>{{ t('crm.prospecting.outreachMarkSent') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" @click="act(() => crmProspectingApi.recordOutreachTouchpoint({ touchpointId: touchpoint.basicId, state: CrmOutreachTouchpointState.Replied, replyText }))">
          <XhButtonLabel>{{ t('crm.prospecting.outreachMarkReplied') }}</XhButtonLabel>
        </XhButton>
      </template>
    </div>
  </section>
</template>

<style scoped>
.outreach-panel {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.outreach-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.outreach-field {
  flex: 1 1 14rem;
}
.outreach-select {
  width: 100%;
}
.outreach-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.outreach-reply-field {
  flex: 1 1 12rem;
  min-width: 0;
}
.outreach-main {
  display: grid;
  gap: 0.2rem;
}
.outreach-main small {
  color: hsl(var(--muted-foreground));
}
.outreach-metrics {
  font-size: 0.8rem;
  color: hsl(var(--muted-foreground));
}
</style>
