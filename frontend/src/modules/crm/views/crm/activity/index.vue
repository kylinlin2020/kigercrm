<script setup lang="ts">
import type { CrmActivityCreateDto, CrmActivityListItemDto, CrmActivityUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmActivityType, crmActivityApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmActivityPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const relatedEntityType = ref('')
const activityTypeFilter = ref<CrmActivityType | ''>('')
const rows = ref<CrmActivityListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive({
  relatedEntityType: 'customer',
  relatedEntityId: '',
  activityType: CrmActivityType.Call,
  subject: '',
  content: '',
  occurredAt: new Date().toISOString().slice(0, 16),
  ownerUserId: '',
  externalSystem: '',
  externalId: '',
})

const canCreate = computed(() => hasPermission('crm_activities.create'))
const canUpdate = computed(() => hasPermission('crm_activities.update'))
const canDelete = computed(() => hasPermission('crm_activities.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function typeLabel(type: CrmActivityType | number) {
  switch (type) {
    case CrmActivityType.Email: return t('crm.activity.email')
    case CrmActivityType.Meeting: return t('crm.activity.meeting')
    case CrmActivityType.Note: return t('crm.activity.note')
    case CrmActivityType.WhatsApp: return t('crm.activity.whatsApp')
    case CrmActivityType.Other: return t('crm.activity.other')
    default: return t('crm.activity.call')
  }
}

function relatedLabel(type?: string | null) {
  switch (type) {
    case 'customer': return t('crm.activity.customer')
    case 'contact': return t('crm.activity.contact')
    case 'lead': return t('crm.activity.lead')
    case 'opportunity': return t('crm.activity.opportunity')
    default: return type || '—'
  }
}

function resetForm() {
  Object.assign(form, {
    relatedEntityType: relatedEntityType.value || 'customer',
    relatedEntityId: '',
    activityType: CrmActivityType.Call,
    subject: '',
    content: '',
    occurredAt: new Date().toISOString().slice(0, 16),
    ownerUserId: '',
    externalSystem: '',
    externalId: '',
  })
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmActivityApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      relatedEntityType: relatedEntityType.value || null,
      activityType: activityTypeFilter.value === '' ? null : activityTypeFilter.value,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.activity.loadError'))
  }
  finally {
    loading.value = false
  }
}

function search() {
  pageIndex.value = 1
  void loadPage()
}

function openCreate() {
  if (!canCreate.value) return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmActivityListItemDto) {
  if (!canUpdate.value) return
  loading.value = true
  try {
    const detail = await crmActivityApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.activity.notFound'))
      return
    }
    editingId.value = detail.basicId
    Object.assign(form, {
      relatedEntityType: detail.relatedEntityType,
      relatedEntityId: detail.relatedEntityId,
      activityType: detail.activityType,
      subject: detail.subject,
      content: detail.content ?? '',
      occurredAt: new Date(detail.occurredAt).toISOString().slice(0, 16),
      ownerUserId: detail.ownerUserId ?? '',
      externalSystem: detail.externalSystem ?? '',
      externalId: detail.externalId ?? '',
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.activity.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function save() {
  if (!form.relatedEntityType || !form.relatedEntityId.trim() || !form.subject.trim()) {
    toast.warning(t('crm.activity.requiredFields'))
    return
  }
  submitting.value = true
  try {
    const common = {
      relatedEntityType: form.relatedEntityType,
      relatedEntityId: form.relatedEntityId.trim(),
      activityType: form.activityType,
      subject: form.subject.trim(),
      content: form.content || null,
      occurredAt: new Date(form.occurredAt).toISOString(),
      ownerUserId: form.ownerUserId.trim() || null,
      externalSystem: form.externalSystem.trim() || null,
      externalId: form.externalId.trim() || null,
    }
    if (editingId.value) {
      const input: CrmActivityUpdateDto = { ...common, basicId: editingId.value }
      await crmActivityApi.update(input)
      toast.success(t('crm.activity.updateSuccess'))
    }
    else {
      const input: CrmActivityCreateDto = common
      await crmActivityApi.create(input)
      toast.success(t('crm.activity.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.activity.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function remove(row: CrmActivityListItemDto) {
  if (!canDelete.value) return
  if (!window.confirm(t('crm.activity.confirmDelete', { name: row.subject }))) return
  try {
    await crmActivityApi.delete(row.basicId)
    toast.success(t('crm.activity.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.activity.deleteError'))
  }
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-activity-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.activity.title') }}</h1>
        <p>{{ t('crm.activity.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">{{ t('crm.activity.create') }}</button>
    </header>
    <section class="toolbar" :aria-label="t('crm.activity.title')">
      <input v-model="keyword" :placeholder="t('crm.activity.search')" :aria-label="t('crm.activity.search')" @keydown.enter="search">
      <select v-model="relatedEntityType" :aria-label="t('crm.activity.relatedType')">
        <option value="">{{ t('crm.activity.allRelations') }}</option>
        <option value="customer">{{ t('crm.activity.customer') }}</option>
        <option value="contact">{{ t('crm.activity.contact') }}</option>
        <option value="lead">{{ t('crm.activity.lead') }}</option>
        <option value="opportunity">{{ t('crm.activity.opportunity') }}</option>
      </select>
      <select v-model="activityTypeFilter" :aria-label="t('crm.activity.type')">
        <option value="">{{ t('crm.activity.allTypes') }}</option>
        <option :value="CrmActivityType.Call">{{ t('crm.activity.call') }}</option>
        <option :value="CrmActivityType.Email">{{ t('crm.activity.email') }}</option>
        <option :value="CrmActivityType.Meeting">{{ t('crm.activity.meeting') }}</option>
        <option :value="CrmActivityType.Note">{{ t('crm.activity.note') }}</option>
        <option :value="CrmActivityType.WhatsApp">{{ t('crm.activity.whatsApp') }}</option>
        <option :value="CrmActivityType.Other">{{ t('crm.activity.other') }}</option>
      </select>
      <button type="button" @click="search">{{ t('crm.activity.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.activity.refresh') }}</button>
    </section>
    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.activity.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.activity.empty') }}</div>
      <table v-else>
        <thead><tr><th>{{ t('crm.activity.subject') }}</th><th>{{ t('crm.activity.type') }}</th><th>{{ t('crm.activity.relatedType') }}</th><th>{{ t('crm.activity.relatedId') }}</th><th>{{ t('crm.activity.occurredAt') }}</th><th>{{ t('crm.activity.actions') }}</th></tr></thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td><strong>{{ row.subject }}</strong></td>
            <td>{{ typeLabel(row.activityType) }}</td>
            <td>{{ relatedLabel(row.relatedEntityType) }}</td>
            <td>{{ row.relatedEntityId }}</td>
            <td>{{ new Date(row.occurredAt).toLocaleString() }}</td>
            <td class="actions">
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.activity.edit') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.activity.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <footer class="pagination">
      <span>{{ t('crm.activity.total', { count: totalCount }) }} · {{ t('crm.activity.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.activity.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.activity.next') }}</button>
      </div>
    </footer>
    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.activity.editTitle') : t('crm.activity.newTitle')">
        <header><h2>{{ editingId ? t('crm.activity.editTitle') : t('crm.activity.newTitle') }}</h2><button type="button" @click="editorOpen = false">×</button></header>
        <form @submit.prevent="save">
          <label>{{ t('crm.activity.relatedType') }} *
            <select v-model="form.relatedEntityType"><option value="customer">{{ t('crm.activity.customer') }}</option><option value="contact">{{ t('crm.activity.contact') }}</option><option value="lead">{{ t('crm.activity.lead') }}</option><option value="opportunity">{{ t('crm.activity.opportunity') }}</option></select>
          </label>
          <label>{{ t('crm.activity.relatedId') }} *<input v-model="form.relatedEntityId" required inputmode="numeric"></label>
          <label>{{ t('crm.activity.type') }}
            <select v-model.number="form.activityType"><option :value="CrmActivityType.Call">{{ t('crm.activity.call') }}</option><option :value="CrmActivityType.Email">{{ t('crm.activity.email') }}</option><option :value="CrmActivityType.Meeting">{{ t('crm.activity.meeting') }}</option><option :value="CrmActivityType.Note">{{ t('crm.activity.note') }}</option><option :value="CrmActivityType.WhatsApp">{{ t('crm.activity.whatsApp') }}</option><option :value="CrmActivityType.Other">{{ t('crm.activity.other') }}</option></select>
          </label>
          <label>{{ t('crm.activity.subject') }} *<input v-model="form.subject" required maxlength="200"></label>
          <label>{{ t('crm.activity.occurredAt') }} *<input v-model="form.occurredAt" type="datetime-local" required></label>
          <label>{{ t('crm.activity.ownerId') }}<input v-model="form.ownerUserId" inputmode="numeric"></label>
          <label>{{ t('crm.activity.externalSystem') }}<input v-model="form.externalSystem" maxlength="80"></label>
          <label>{{ t('crm.activity.externalId') }}<input v-model="form.externalId" maxlength="160"></label>
          <label class="wide">{{ t('crm.activity.content') }}<textarea v-model="form.content" rows="4" /></label>
          <footer><button type="button" @click="editorOpen = false">{{ t('crm.activity.cancel') }}</button><button class="primary-button" type="submit" :disabled="submitting">{{ submitting ? t('crm.activity.saving') : t('crm.activity.save') }}</button></footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-activity-page { display:flex; flex-direction:column; gap:16px; min-height:100%; padding:20px; color:hsl(var(--foreground)); }
.page-header,.toolbar,.pagination,.editor header,.editor footer { display:flex; align-items:center; justify-content:space-between; gap:12px; }
.page-header h1,.editor h2 { margin:0; font-size:20px; font-weight:650; }
.page-header p { margin:5px 0 0; color:hsl(var(--muted-foreground)); font-size:12px; }
button,input,select,textarea { font:inherit; }
button { border:1px solid hsl(var(--border)); border-radius:7px; padding:7px 11px; background:hsl(var(--card)); cursor:pointer; }
button:disabled { cursor:not-allowed; opacity:.55; }
.primary-button { border-color:hsl(var(--primary)); background:hsl(var(--primary)); color:hsl(var(--primary-foreground)); }
.toolbar { justify-content:flex-start; flex-wrap:wrap; }
.toolbar input,.toolbar select,.editor input,.editor select,.editor textarea { border:1px solid hsl(var(--border)); border-radius:7px; padding:8px 10px; background:hsl(var(--card)); color:inherit; }
.table-wrap { overflow:auto; border:1px solid hsl(var(--border)); border-radius:10px; background:hsl(var(--card)); }
table { width:100%; border-collapse:collapse; min-width:900px; text-align:left; font-size:13px; }
th,td { padding:11px 12px; border-bottom:1px solid hsl(var(--border)); vertical-align:middle; }
th { color:hsl(var(--muted-foreground)); font-size:11px; font-weight:600; white-space:nowrap; }
.actions { white-space:nowrap; }
.actions button { margin-right:5px; padding:5px 8px; font-size:11px; }
.danger-action { color:var(--xh-color-danger-600); }
.empty-state { padding:44px 16px; text-align:center; color:hsl(var(--muted-foreground)); }
.pagination { color:hsl(var(--muted-foreground)); font-size:12px; }
.pagination div { display:flex; gap:6px; }
.modal-backdrop { position:fixed; z-index:1000; inset:0; display:grid; place-items:center; padding:20px; background:rgb(0 0 0 / 42%); }
.editor { width:min(760px,100%); max-height:min(90vh,900px); overflow:auto; border:1px solid hsl(var(--border)); border-radius:12px; padding:20px; background:hsl(var(--card)); box-shadow:0 20px 70px rgb(0 0 0 / 20%); }
.editor form { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:13px; margin-top:18px; }
.editor label { display:flex; flex-direction:column; gap:6px; font-size:12px; color:hsl(var(--muted-foreground)); }
.editor label input,.editor label select,.editor label textarea { width:100%; box-sizing:border-box; color:hsl(var(--foreground)); }
.editor .wide,.editor form footer { grid-column:1/-1; }
.editor form footer { justify-content:flex-end; }
@media(max-width:680px) { .crm-activity-page { padding:12px; } .editor form { grid-template-columns:1fr; } .editor .wide,.editor form footer { grid-column:auto; } }
</style>
