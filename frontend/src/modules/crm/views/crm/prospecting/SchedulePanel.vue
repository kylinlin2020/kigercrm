<script setup lang="ts">
import type {
  CrmProspectingCampaignDto,
  CrmProspectingScheduleDto,
} from '@/api'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  crmProspectingApi,
  CrmProspectingScheduleFrequency,
  CrmProspectingScheduleStatus,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingSchedulePanel' })

const emit = defineEmits<{ scheduleChanged: [] }>()

const { t } = useI18n()
const { hasPermission } = usePermission()
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))

const campaigns = ref<CrmProspectingCampaignDto[]>([])
const campaignId = ref('')
const schedules = ref<CrmProspectingScheduleDto[]>([])
const loading = ref(false)

const name = ref('')
const requestKeyPrefix = ref('')
const timeZoneId = ref('UTC')
const frequency = ref(CrmProspectingScheduleFrequency.Daily)
const interval = ref(1)
const timeOfDayMinutes = ref(9 * 60)
const dayOfWeek = ref('')
const dayOfMonth = ref('')
const startDate = ref('')
const endDate = ref('')

function statusLabel(value: CrmProspectingScheduleStatus) {
  return t(`crm.prospecting.scheduleStatus${value}`)
}

function formatTime(value?: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}

async function loadCampaigns() {
  try {
    campaigns.value = await crmProspectingApi.campaigns()
    if (!campaigns.value.some(item => String(item.basicId) === campaignId.value)) {
      campaignId.value = campaigns.value[0] ? String(campaigns.value[0].basicId) : ''
    }
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
}

async function loadSchedules() {
  if (!campaignId.value) {
    schedules.value = []
    return
  }
  loading.value = true
  try {
    schedules.value = await crmProspectingApi.schedules(campaignId.value)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
  finally {
    loading.value = false
  }
}

onMounted(async () => {
  await loadCampaigns()
  await loadSchedules()
})

watch(campaignId, () => {
  void loadSchedules()
})

async function afterChange(action: () => Promise<unknown>) {
  try {
    await action()
    emit('scheduleChanged')
    await loadSchedules()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.scheduleError'))
  }
}

function createSchedule() {
  if (!canExecute.value || !campaignId.value) {
    return
  }
  const start = startDate.value ? new Date(startDate.value).toISOString() : new Date().toISOString()
  const end = endDate.value ? new Date(endDate.value).toISOString() : null
  void afterChange(() => crmProspectingApi.createSchedule({
    campaignId: campaignId.value,
    name: name.value,
    requestKeyPrefix: requestKeyPrefix.value,
    timeZoneId: timeZoneId.value,
    frequency: Number(frequency.value) as CrmProspectingScheduleFrequency,
    interval: Number(interval.value),
    timeOfDayMinutes: Number(timeOfDayMinutes.value),
    dayOfWeek: dayOfWeek.value === '' ? null : Number(dayOfWeek.value),
    dayOfMonth: dayOfMonth.value === '' ? null : Number(dayOfMonth.value),
    startDateUtc: start,
    endDateUtc: end,
  }))
}

function submit(scheduleId: string) {
  void afterChange(() => crmProspectingApi.submitSchedule(scheduleId))
}

function decide(scheduleId: string, approve: boolean) {
  void afterChange(() => crmProspectingApi.decideSchedule({ scheduleId, approve }))
}

function pause(scheduleId: string) {
  void afterChange(() => crmProspectingApi.pauseSchedule(scheduleId))
}

function resume(scheduleId: string) {
  void afterChange(() => crmProspectingApi.resumeSchedule(scheduleId))
}

function cancel(scheduleId: string) {
  void afterChange(() => crmProspectingApi.cancelSchedule(scheduleId))
}
</script>

<template>
  <section class="schedule-panel">
    <header class="section-header">
      <div>
        <h2>{{ t('crm.prospecting.scheduleTitle') }}</h2>
        <p>{{ t('crm.prospecting.scheduleDescription') }}</p>
      </div>
      <label>{{ t('crm.prospecting.campaign') }}
        <select v-model="campaignId">
          <option value="">{{ t('crm.prospecting.selectCampaign') }}</option>
          <option v-for="campaign in campaigns" :key="campaign.basicId" :value="String(campaign.basicId)">
            {{ campaign.name }}
          </option>
        </select>
      </label>
    </header>

    <div class="schedule-form">
      <label>{{ t('crm.prospecting.scheduleName') }}
        <input v-model="name" type="text" maxlength="160">
      </label>
      <label>{{ t('crm.prospecting.scheduleRequestKey') }}
        <input v-model="requestKeyPrefix" type="text" maxlength="80">
      </label>
      <label>{{ t('crm.prospecting.scheduleTimeZone') }}
        <input v-model="timeZoneId" type="text" maxlength="100">
      </label>
      <label>{{ t('crm.prospecting.scheduleFrequency') }}
        <select v-model.number="frequency">
          <option :value="CrmProspectingScheduleFrequency.Daily">{{ t('crm.prospecting.scheduleDaily') }}</option>
          <option :value="CrmProspectingScheduleFrequency.Weekly">{{ t('crm.prospecting.scheduleWeekly') }}</option>
          <option :value="CrmProspectingScheduleFrequency.Monthly">{{ t('crm.prospecting.scheduleMonthly') }}</option>
        </select>
      </label>
      <label>{{ t('crm.prospecting.scheduleInterval') }}
        <input v-model.number="interval" type="number" min="1" max="365">
      </label>
      <label>{{ t('crm.prospecting.scheduleTimeOfDay') }}
        <input v-model.number="timeOfDayMinutes" type="number" min="0" max="1439">
      </label>
      <label v-if="frequency === CrmProspectingScheduleFrequency.Weekly">
        {{ t('crm.prospecting.scheduleDayOfWeek') }}
        <input v-model="dayOfWeek" type="number" min="0" max="6">
      </label>
      <label v-if="frequency === CrmProspectingScheduleFrequency.Monthly">
        {{ t('crm.prospecting.scheduleDayOfMonth') }}
        <input v-model="dayOfMonth" type="number" min="-1" max="31">
      </label>
      <label>{{ t('crm.prospecting.scheduleStart') }}
        <input v-model="startDate" type="datetime-local">
      </label>
      <label>{{ t('crm.prospecting.scheduleEnd') }}
        <input v-model="endDate" type="datetime-local">
      </label>
      <button
        v-if="canExecute"
        class="primary-button"
        type="button"
        :disabled="!campaignId"
        @click="createSchedule"
      >
        {{ t('crm.prospecting.scheduleCreate') }}
      </button>
    </div>

    <p v-if="loading" class="schedule-empty">
      {{ t('crm.prospecting.scheduleLoading') }}
    </p>
    <p v-else-if="schedules.length === 0" class="schedule-empty">
      {{ t('crm.prospecting.scheduleEmpty') }}
    </p>
    <div v-for="schedule in schedules" :key="schedule.basicId" class="schedule-row">
      <div class="schedule-main">
        <strong>{{ schedule.name }}</strong>
        <small>
          {{ schedule.requestKeyPrefix }} · {{ statusLabel(schedule.status) }}
          · {{ t('crm.prospecting.scheduleNextRun') }}: {{ formatTime(schedule.nextRunAt) }}
        </small>
      </div>
      <div class="schedule-actions">
        <button
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Draft"
          type="button"
          @click="submit(schedule.basicId)"
        >
          {{ t('crm.prospecting.scheduleSubmit') }}
        </button>
        <button
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.PendingApproval"
          type="button"
          @click="decide(schedule.basicId, true)"
        >
          {{ t('crm.prospecting.approve') }}
        </button>
        <button
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.PendingApproval"
          type="button"
          @click="decide(schedule.basicId, false)"
        >
          {{ t('crm.prospecting.reject') }}
        </button>
        <button
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Approved"
          type="button"
          @click="pause(schedule.basicId)"
        >
          {{ t('crm.prospecting.schedulePause') }}
        </button>
        <button
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Paused"
          type="button"
          @click="resume(schedule.basicId)"
        >
          {{ t('crm.prospecting.scheduleResume') }}
        </button>
        <button
          v-if="canExecute && schedule.status !== CrmProspectingScheduleStatus.Cancelled && schedule.status !== CrmProspectingScheduleStatus.Rejected"
          type="button"
          @click="cancel(schedule.basicId)"
        >
          {{ t('crm.prospecting.scheduleCancel') }}
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.schedule-panel {
  display: grid;
  gap: 0.75rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.schedule-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.schedule-form label {
  display: grid;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.schedule-form input,
.schedule-form select,
.section-header select {
  width: 100%;
  font: inherit;
}
.schedule-empty {
  margin: 0;
  color: var(--xh-fg-muted, #777);
  font-size: 0.85rem;
}
.schedule-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.6rem;
  padding: 0.5rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.schedule-main {
  display: grid;
  gap: 0.2rem;
}
.schedule-main small {
  color: var(--xh-fg-muted, #777);
}
.schedule-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
</style>
