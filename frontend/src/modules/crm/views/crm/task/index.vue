<script setup lang="ts">
import type { CrmTaskCreateDto, CrmTaskListItemDto, CrmTaskUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmTaskStatus, crmTaskApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmTaskPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const statusFilter = ref<CrmTaskStatus | ''>('')
const overdueOnly = ref(false)
const rows = ref<CrmTaskListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive({
  relatedEntityType: '',
  relatedEntityId: '',
  title: '',
  description: '',
  dueAt: '',
  status: CrmTaskStatus.Open,
  priority: 3,
  assigneeUserId: '',
})

const canCreate = computed(() => hasPermission('crm_tasks.create'))
const canUpdate = computed(() => hasPermission('crm_tasks.update'))
const canDelete = computed(() => hasPermission('crm_tasks.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function statusLabel(status: CrmTaskStatus | number) {
  switch (status) {
    case CrmTaskStatus.InProgress: return t('crm.task.inProgress')
    case CrmTaskStatus.Completed: return t('crm.task.completed')
    case CrmTaskStatus.Cancelled: return t('crm.task.cancelled')
    default: return t('crm.task.open')
  }
}

function priorityLabel(priority: number) {
  return t(`crm.task.priority${priority}`)
}

function resetForm() {
  Object.assign(form, {
    relatedEntityType: '',
    relatedEntityId: '',
    title: '',
    description: '',
    dueAt: '',
    status: CrmTaskStatus.Open,
    priority: 3,
    assigneeUserId: '',
  })
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmTaskApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      status: statusFilter.value === '' ? null : statusFilter.value,
      overdueOnly: overdueOnly.value || null,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.task.loadError'))
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

async function openEdit(row: CrmTaskListItemDto) {
  if (!canUpdate.value) return
  loading.value = true
  try {
    const detail = await crmTaskApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.task.notFound'))
      return
    }
    resetForm()
    editingId.value = detail.basicId
    Object.assign(form, {
      relatedEntityType: detail.relatedEntityType ?? '',
      relatedEntityId: detail.relatedEntityId ?? '',
      title: detail.title,
      description: detail.description ?? '',
      dueAt: detail.dueAt ? new Date(detail.dueAt).toISOString().slice(0, 16) : '',
      status: detail.status,
      priority: detail.priority,
      assigneeUserId: detail.assigneeUserId ?? '',
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.task.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function save() {
  if (!form.title.trim()) {
    toast.warning(t('crm.task.titleRequired'))
    return
  }
  if (Boolean(form.relatedEntityType) !== Boolean(form.relatedEntityId.trim())) {
    toast.warning(t('crm.task.relatedPairRequired'))
    return
  }
  if (!Number.isInteger(form.priority) || form.priority < 1 || form.priority > 5) {
    toast.warning(t('crm.task.priorityInvalid'))
    return
  }
  submitting.value = true
  try {
    const common = {
      relatedEntityType: form.relatedEntityType || null,
      relatedEntityId: form.relatedEntityId.trim() || null,
      title: form.title.trim(),
      description: form.description || null,
      dueAt: form.dueAt ? new Date(form.dueAt).toISOString() : null,
      priority: form.priority,
      assigneeUserId: form.assigneeUserId.trim() || null,
    }
    if (editingId.value) {
      const input: CrmTaskUpdateDto = { ...common, basicId: editingId.value, status: form.status }
      await crmTaskApi.update(input)
      toast.success(t('crm.task.updateSuccess'))
    }
    else {
      const input: CrmTaskCreateDto = common
      await crmTaskApi.create(input)
      toast.success(t('crm.task.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.task.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function changeStatus(row: CrmTaskListItemDto, status: CrmTaskStatus) {
  if (!canUpdate.value || row.status === status) return
  try {
    await crmTaskApi.changeStatus({ id: row.basicId, status })
    toast.success(t('crm.task.statusSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.task.statusError'))
  }
}

async function remove(row: CrmTaskListItemDto) {
  if (!canDelete.value) return
  if (!window.confirm(t('crm.task.confirmDelete', { name: row.title }))) return
  try {
    await crmTaskApi.delete(row.basicId)
    toast.success(t('crm.task.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.task.deleteError'))
  }
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-task-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.task.title') }}</h1>
        <p>{{ t('crm.task.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">
        {{ t('crm.task.create') }}
      </button>
    </header>

    <section class="toolbar" :aria-label="t('crm.task.title')">
      <input
        v-model="keyword"
        :aria-label="t('crm.task.search')"
        :placeholder="t('crm.task.search')"
        @keydown.enter="search"
      >
      <select v-model="statusFilter" :aria-label="t('crm.task.status')">
        <option value="">{{ t('crm.task.allStatuses') }}</option>
        <option :value="CrmTaskStatus.Open">{{ t('crm.task.open') }}</option>
        <option :value="CrmTaskStatus.InProgress">{{ t('crm.task.inProgress') }}</option>
        <option :value="CrmTaskStatus.Completed">{{ t('crm.task.completed') }}</option>
        <option :value="CrmTaskStatus.Cancelled">{{ t('crm.task.cancelled') }}</option>
      </select>
      <label class="overdue-filter"><input v-model="overdueOnly" type="checkbox"> {{ t('crm.task.overdueOnly') }}</label>
      <button type="button" @click="search">{{ t('crm.task.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.task.refresh') }}</button>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.task.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.task.empty') }}</div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.task.titleField') }}</th>
            <th>{{ t('crm.task.relatedType') }}</th>
            <th>{{ t('crm.task.relatedId') }}</th>
            <th>{{ t('crm.task.dueAt') }}</th>
            <th>{{ t('crm.task.status') }}</th>
            <th>{{ t('crm.task.priority') }}</th>
            <th>{{ t('crm.task.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td><strong>{{ row.title }}</strong></td>
            <td>{{ row.relatedEntityType || '—' }}</td>
            <td>{{ row.relatedEntityId || '—' }}</td>
            <td>{{ row.dueAt ? new Date(row.dueAt).toLocaleString() : '—' }}</td>
            <td><span class="status-pill" :data-status="row.status">{{ statusLabel(row.status) }}</span></td>
            <td>{{ priorityLabel(row.priority) }}</td>
            <td class="actions">
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.task.edit') }}</button>
              <button
                v-if="canUpdate && row.status === CrmTaskStatus.Open"
                type="button"
                @click="changeStatus(row, CrmTaskStatus.InProgress)"
              >{{ t('crm.task.inProgress') }}</button>
              <button
                v-if="canUpdate && row.status !== CrmTaskStatus.Completed && row.status !== CrmTaskStatus.Cancelled"
                type="button"
                @click="changeStatus(row, CrmTaskStatus.Completed)"
              >{{ t('crm.task.completed') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.task.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.task.total', { count: totalCount }) }} · {{ t('crm.task.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.task.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.task.next') }}</button>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.task.editTitle') : t('crm.task.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.task.editTitle') : t('crm.task.newTitle') }}</h2>
          <button type="button" :aria-label="t('common.actions.close')" @click="editorOpen = false">×</button>
        </header>
        <form @submit.prevent="save">
          <label>{{ t('crm.task.titleField') }} *<input v-model="form.title" required maxlength="200"></label>
          <label>{{ t('crm.task.relatedType') }}
            <select v-model="form.relatedEntityType">
              <option value="">{{ t('crm.task.none') }}</option>
              <option value="customer">{{ t('crm.task.customer') }}</option>
              <option value="contact">{{ t('crm.task.contact') }}</option>
              <option value="lead">{{ t('crm.task.lead') }}</option>
              <option value="opportunity">{{ t('crm.task.opportunity') }}</option>
            </select>
          </label>
          <label>{{ t('crm.task.relatedId') }}<input v-model="form.relatedEntityId" inputmode="numeric"></label>
          <label>{{ t('crm.task.dueAt') }}<input v-model="form.dueAt" type="datetime-local"></label>
          <label>{{ t('crm.task.priority') }}
            <select v-model.number="form.priority">
              <option :value="1">{{ t('crm.task.priority1') }}</option>
              <option :value="2">{{ t('crm.task.priority2') }}</option>
              <option :value="3">{{ t('crm.task.priority3') }}</option>
              <option :value="4">{{ t('crm.task.priority4') }}</option>
              <option :value="5">{{ t('crm.task.priority5') }}</option>
            </select>
          </label>
          <label>{{ t('crm.task.assignee') }}<input v-model="form.assigneeUserId" inputmode="numeric"></label>
          <label v-if="editingId">{{ t('crm.task.status') }}
            <select v-model.number="form.status">
              <option :value="CrmTaskStatus.Open">{{ t('crm.task.open') }}</option>
              <option :value="CrmTaskStatus.InProgress">{{ t('crm.task.inProgress') }}</option>
              <option :value="CrmTaskStatus.Completed">{{ t('crm.task.completed') }}</option>
              <option :value="CrmTaskStatus.Cancelled">{{ t('crm.task.cancelled') }}</option>
            </select>
          </label>
          <label class="wide">{{ t('crm.task.descriptionField') }}<textarea v-model="form.description" rows="3" /></label>
          <footer>
            <button type="button" @click="editorOpen = false">{{ t('crm.task.cancel') }}</button>
            <button class="primary-button" type="submit" :disabled="submitting">
              {{ submitting ? t('crm.task.saving') : t('crm.task.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-task-page { display: flex; flex-direction: column; gap: 16px; min-height: 100%; padding: 20px; color: hsl(var(--foreground)); }
.page-header, .toolbar, .pagination, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.page-header h1, .editor h2 { margin: 0; font-size: 20px; font-weight: 650; }
.page-header p { margin: 5px 0 0; color: hsl(var(--muted-foreground)); font-size: 12px; }
button, input, select, textarea { font: inherit; }
button { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 7px 11px; background: hsl(var(--card)); cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.primary-button { border-color: hsl(var(--primary)); background: hsl(var(--primary)); color: hsl(var(--primary-foreground)); }
.toolbar { justify-content: flex-start; flex-wrap: wrap; }
.toolbar > input { min-width: 240px; }
.toolbar input, .toolbar select, .editor input, .editor select, .editor textarea { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 8px 10px; background: hsl(var(--card)); color: inherit; }
.overdue-filter { display: flex; align-items: center; gap: 6px; font-size: 12px; }
.overdue-filter input { width: auto; }
.table-wrap { overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 10px; background: hsl(var(--card)); }
table { width: 100%; border-collapse: collapse; min-width: 900px; text-align: left; font-size: 13px; }
th, td { padding: 11px 12px; border-bottom: 1px solid hsl(var(--border)); vertical-align: middle; }
th { color: hsl(var(--muted-foreground)); font-size: 11px; font-weight: 600; white-space: nowrap; }
.actions { white-space: nowrap; }
.actions button { margin-right: 5px; padding: 5px 8px; font-size: 11px; }
.danger-action { color: var(--xh-color-danger-600); }
.status-pill { display: inline-block; padding: 3px 8px; border-radius: 999px; background: hsl(var(--muted)); font-size: 11px; white-space: nowrap; }
.status-pill[data-status="2"] { color: var(--xh-color-success-600); }
.status-pill[data-status="3"] { color: var(--xh-color-danger-600); }
.empty-state { padding: 44px 16px; text-align: center; color: hsl(var(--muted-foreground)); }
.pagination { color: hsl(var(--muted-foreground)); font-size: 12px; }
.pagination div { display: flex; gap: 6px; }
.modal-backdrop { position: fixed; z-index: 1000; inset: 0; display: grid; place-items: center; padding: 20px; background: rgb(0 0 0 / 42%); }
.editor { width: min(760px, 100%); max-height: min(90vh, 900px); overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 12px; padding: 20px; background: hsl(var(--card)); box-shadow: 0 20px 70px rgb(0 0 0 / 20%); }
.editor form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 13px; margin-top: 18px; }
.editor label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: hsl(var(--muted-foreground)); }
.editor label input, .editor label select, .editor label textarea { width: 100%; box-sizing: border-box; color: hsl(var(--foreground)); }
.editor .wide, .editor form footer { grid-column: 1 / -1; }
.editor form footer { justify-content: flex-end; }
@media (max-width: 680px) {
  .crm-task-page { padding: 12px; }
  .editor form { grid-template-columns: 1fr; }
  .editor .wide, .editor form footer { grid-column: auto; }
}
</style>
