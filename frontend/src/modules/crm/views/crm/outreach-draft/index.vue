<script setup lang="ts">
import type { CrmOutreachDraftDto } from '@/api/modules/crm/outreach-draft.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmOutreachDraftApi } from '@/api/modules/crm/outreach-draft'
import { CrmOutreachDraftStatus } from '@/api/modules/crm/outreach-draft.types'
import { XInput } from '~/components'
import { toast } from '~/composables'

defineOptions({ name: 'CrmOutreachDraftPage' })

const { t } = useI18n()
const candidateId = ref('')
const template = ref('Hi {{FirstName}} at {{CompanyName}}')
const preview = ref('')
const enrollmentId = ref('')
const step = ref('0')
const drafts = ref<CrmOutreachDraftDto[]>([])
const reason = ref('')
const busy = ref(false)

function statusLabel(status: CrmOutreachDraftStatus) {
  return t(`crm.outreachDraft.status.${CrmOutreachDraftStatus[status]}`)
}

async function act(task: () => Promise<unknown>, successKey?: string) {
  busy.value = true
  try {
    await task()
    if (successKey)
      toast.success(t(successKey))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.outreachDraft.error'))
  }
  finally {
    busy.value = false
  }
}

async function renderPreview() {
  if (!candidateId.value)
    return
  await act(async () => {
    preview.value = await crmOutreachDraftApi.preview({
      candidateId: candidateId.value,
      template: template.value,
    })
  })
}

async function loadDrafts() {
  if (!enrollmentId.value)
    return
  await act(async () => {
    drafts.value = await crmOutreachDraftApi.list(enrollmentId.value)
  })
}

async function createDraft() {
  if (!enrollmentId.value)
    return
  await act(async () => {
    await crmOutreachDraftApi.create({ enrollmentId: enrollmentId.value, step: Number(step.value) || 0 })
    toast.success(t('crm.outreachDraft.created'))
    await loadDrafts()
  })
}

async function approve(draftId: string) {
  await act(async () => {
    const updated = await crmOutreachDraftApi.approve(draftId)
    const target = drafts.value.find(item => item.basicId === draftId)
    if (target)
      Object.assign(target, updated)
    toast.success(t('crm.outreachDraft.approved'))
  })
}

async function reject(draftId: string) {
  if (!reason.value)
    return
  await act(async () => {
    const updated = await crmOutreachDraftApi.reject({ draftId, reason: reason.value })
    const target = drafts.value.find(item => item.basicId === draftId)
    if (target)
      Object.assign(target, updated)
    reason.value = ''
    toast.success(t('crm.outreachDraft.rejected'))
  })
}
</script>

<template>
  <main class="outreach-draft-page">
    <header class="page-header">
      <h1>{{ t('crm.outreachDraft.title') }}</h1>
      <p>{{ t('crm.outreachDraft.description') }}</p>
    </header>

    <section class="draft-section">
      <h2>{{ t('crm.outreachDraft.previewTitle') }}</h2>
      <div class="draft-form">
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.outreachDraft.candidateId') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="candidateId" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot class="draft-template">
          <XhFieldLabel>{{ t('crm.outreachDraft.template') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="template" type="textarea" :rows="2" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="outline" :disabled="!candidateId || busy" @click="renderPreview">
          <XhButtonLabel>{{ t('crm.outreachDraft.preview') }}</XhButtonLabel>
        </XhButton>
      </div>
      <p v-if="preview" class="draft-preview">
        {{ preview }}
      </p>
    </section>

    <section class="draft-section">
      <h2>{{ t('crm.outreachDraft.draftsTitle') }}</h2>
      <div class="draft-form">
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.outreachDraft.enrollmentId') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="enrollmentId" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.outreachDraft.step') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="step" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="outline" :disabled="!enrollmentId || busy" @click="createDraft">
          <XhButtonLabel>{{ t('crm.outreachDraft.create') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="!enrollmentId || busy" @click="loadDrafts">
          <XhButtonLabel>{{ t('crm.outreachDraft.refresh') }}</XhButtonLabel>
        </XhButton>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.outreachDraft.reason') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="reason" />
          </XhFieldControl>
        </XhFieldRoot>
      </div>
      <p v-if="drafts.length === 0">
        {{ t('crm.outreachDraft.noDrafts') }}
      </p>
      <div v-for="item in drafts" :key="item.basicId" class="draft-row">
        <span class="draft-text">{{ item.renderedText }}</span>
        <span>{{ statusLabel(item.status) }}</span>
        <template v-if="item.status === CrmOutreachDraftStatus.Planned">
          <XhButton variant="outline" :disabled="busy" @click="approve(item.basicId)">
            <XhButtonLabel>{{ t('crm.outreachDraft.approve') }}</XhButtonLabel>
          </XhButton>
          <XhButton variant="subtle" :disabled="!reason || busy" @click="reject(item.basicId)">
            <XhButtonLabel>{{ t('crm.outreachDraft.reject') }}</XhButtonLabel>
          </XhButton>
        </template>
      </div>
    </section>
  </main>
</template>

<style scoped>
.outreach-draft-page {
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.35rem;
}
.page-header p {
  margin: 0.25rem 0 0;
  color: hsl(var(--muted-foreground));
}
.draft-section {
  display: grid;
  gap: 0.5rem;
}
.draft-section h2 {
  margin: 0;
  font-size: 1rem;
}
.draft-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.draft-template {
  flex: 1 1 320px;
}
.draft-preview,
.draft-text {
  color: hsl(var(--foreground));
}
.draft-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.draft-text {
  flex: 1 1 260px;
}
</style>
