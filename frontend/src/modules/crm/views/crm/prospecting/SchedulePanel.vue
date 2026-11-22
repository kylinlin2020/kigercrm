<script setup lang="ts">
import type {
  CrmProspectingCampaignDto,
  CrmProspectingScheduleDto,
} from '@/api'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  crmProspectingApi,
  CrmProspectingScheduleFrequency,
  CrmProspectingScheduleStatus,
} from '@/api'
import { XInput, XSelect } from '~/components'
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
const interval = ref('1')
const timeOfDayMinutes = ref(String(9 * 60))
const dayOfWeek = ref('')
const dayOfMonth = ref('')
const startDate = ref('')
const endDate = ref('')

const campaignOptions = computed(() => [
  { label: t('crm.prospecting.selectCampaign'), value: '' },
  ...campaigns.value.map(campaign => ({ label: campaign.name, value: String(campaign.basicId) })),
])

const frequencyOptions = computed(() => [
  { label: t('crm.prospecting.scheduleDaily'), value: CrmProspectingScheduleFrequency.Daily },
  { label: t('crm.prospecting.scheduleWeekly'), value: CrmProspectingScheduleFrequency.Weekly },
  { label: t('crm.prospecting.scheduleMonthly'), value: CrmProspectingScheduleFrequency.Monthly },
])

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
      <XhFieldRoot class="campaign-field">
        <XhFieldLabel>{{ t('crm.prospecting.campaign') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="campaignId" :options="campaignOptions" />
        </XhFieldControl>
      </XhFieldRoot>
    </header>

    <div class="schedule-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="name" :max-length="160" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleRequestKey') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="requestKeyPrefix" :max-length="80" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleTimeZone') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="timeZoneId" :max-length="100" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleFrequency') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="frequency" :options="frequencyOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleInterval') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="interval" inputmode="numeric" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleTimeOfDay') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="timeOfDayMinutes" inputmode="numeric" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot v-if="frequency === CrmProspectingScheduleFrequency.Weekly">
        <XhFieldLabel>{{ t('crm.prospecting.scheduleDayOfWeek') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="dayOfWeek" inputmode="numeric" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot v-if="frequency === CrmProspectingScheduleFrequency.Monthly">
        <XhFieldLabel>{{ t('crm.prospecting.scheduleDayOfMonth') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="dayOfMonth" inputmode="numeric" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleStart') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="startDate" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.scheduleEnd') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="endDate" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton
        v-if="canExecute"
        variant="outline"
        :disabled="!campaignId"
        @click="createSchedule"
      >
        <XhButtonLabel>{{ t('crm.prospecting.scheduleCreate') }}</XhButtonLabel>
      </XhButton>
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
        <XhButton
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Draft"
          variant="outline"
          @click="submit(schedule.basicId)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.scheduleSubmit') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.PendingApproval"
          variant="outline"
          @click="decide(schedule.basicId, true)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.approve') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.PendingApproval"
          variant="subtle"
          @click="decide(schedule.basicId, false)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.reject') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Approved"
          variant="subtle"
          @click="pause(schedule.basicId)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.schedulePause') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          v-if="canExecute && schedule.status === CrmProspectingScheduleStatus.Paused"
          variant="outline"
          @click="resume(schedule.basicId)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.scheduleResume') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          v-if="canExecute && schedule.status !== CrmProspectingScheduleStatus.Cancelled && schedule.status !== CrmProspectingScheduleStatus.Rejected"
          variant="subtle"
          @click="cancel(schedule.basicId)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.scheduleCancel') }}</XhButtonLabel>
        </XhButton>
      </div>
    </div>
  </section>
</template>

<style scoped>
.schedule-panel {
  display: grid;
  gap: 0.75rem;
  padding: 0.85rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--xh-radius-md);
  background: hsl(var(--muted));
}
.schedule-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.campaign-field {
  min-width: 12rem;
}
.schedule-empty {
  margin: 0;
  color: hsl(var(--muted-foreground));
  font-size: 0.85rem;
}
.schedule-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.6rem;
  padding: 0.5rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.schedule-main {
  display: grid;
  gap: 0.2rem;
}
.schedule-main small {
  color: hsl(var(--muted-foreground));
}
.schedule-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
</style>
