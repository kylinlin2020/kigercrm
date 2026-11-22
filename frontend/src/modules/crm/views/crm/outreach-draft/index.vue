<script setup lang="ts">
import type { CrmOutreachDraftDto } from '@/api/modules/crm/outreach-draft.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmOutreachDraftApi } from '@/api/modules/crm/outreach-draft'
import { CrmOutreachDraftStatus } from '@/api/modules/crm/outreach-draft.types'
import { toast } from '~/composables'

defineOptions({ name: 'CrmOutreachDraftPage' })

const { t } = useI18n()
const candidateId = ref('')
const template = ref('Hi {{FirstName}} at {{CompanyName}}')
const preview = ref('')
const enrollmentId = ref('')
const step = ref(0)
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
    await crmOutreachDraftApi.create({ enrollmentId: enrollmentId.value, step: step.value })
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
        <label>{{ t('crm.outreachDraft.candidateId') }}
          <input v-model="candidateId" type="text">
        </label>
        <label class="draft-template">{{ t('crm.outreachDraft.template') }}
          <textarea v-model="template" rows="2" />
        </label>
        <button type="button" :disabled="!candidateId || busy" @click="renderPreview">
          {{ t('crm.outreachDraft.preview') }}
        </button>
      </div>
      <p v-if="preview" class="draft-preview">
        {{ preview }}
      </p>
    </section>

    <section class="draft-section">
      <h2>{{ t('crm.outreachDraft.draftsTitle') }}</h2>
      <div class="draft-form">
        <label>{{ t('crm.outreachDraft.enrollmentId') }}
          <input v-model="enrollmentId" type="text">
        </label>
        <label>{{ t('crm.outreachDraft.step') }}
          <input v-model.number="step" type="number" min="0">
        </label>
        <button type="button" :disabled="!enrollmentId || busy" @click="createDraft">
          {{ t('crm.outreachDraft.create') }}
        </button>
        <button type="button" :disabled="!enrollmentId || busy" @click="loadDrafts">
          {{ t('crm.outreachDraft.refresh') }}
        </button>
        <label>{{ t('crm.outreachDraft.reason') }}
          <input v-model="reason" type="text">
        </label>
      </div>
      <p v-if="drafts.length === 0">
        {{ t('crm.outreachDraft.noDrafts') }}
      </p>
      <div v-for="item in drafts" :key="item.basicId" class="draft-row">
        <span class="draft-text">{{ item.renderedText }}</span>
        <span>{{ statusLabel(item.status) }}</span>
        <template v-if="item.status === CrmOutreachDraftStatus.Planned">
          <button type="button" :disabled="busy" @click="approve(item.basicId)">
            {{ t('crm.outreachDraft.approve') }}
          </button>
          <button type="button" :disabled="!reason || busy" @click="reject(item.basicId)">
            {{ t('crm.outreachDraft.reject') }}
          </button>
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
.draft-form label {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.draft-form input,
.draft-form textarea {
  font: inherit;
}
.draft-template {
  flex: 1 1 320px;
}
.draft-template textarea {
  width: 100%;
  resize: vertical;
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
