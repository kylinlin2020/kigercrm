<script setup lang="ts">
import type { CrmTaskCreateDto, CrmTaskListItemDto, CrmTaskUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhCheckbox, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmTaskApi, CrmTaskStatus } from '@/api'
import { XDatePicker, XInput, XSelect } from '~/components'
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

const statusFilterOptions = computed(() => [
  { label: t('crm.task.allStatuses'), value: '' },
  { label: t('crm.task.open'), value: String(CrmTaskStatus.Open) },
  { label: t('crm.task.inProgress'), value: String(CrmTaskStatus.InProgress) },
  { label: t('crm.task.completed'), value: String(CrmTaskStatus.Completed) },
  { label: t('crm.task.cancelled'), value: String(CrmTaskStatus.Cancelled) },
])
// XSelect 收发字符串选项；这里把值还原成 number | ''，保持原 v-model.number 的数字枚举语义。
const statusFilterValue = computed({
  get: () => String(statusFilter.value),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    statusFilter.value = next == null || next === '' ? '' : Number(next) as CrmTaskStatus
  },
})
const relatedTypeOptions = computed(() => [
  { label: t('crm.task.none'), value: '' },
  { label: t('crm.task.customer'), value: 'customer' },
  { label: t('crm.task.contact'), value: 'contact' },
  { label: t('crm.task.lead'), value: 'lead' },
  { label: t('crm.task.opportunity'), value: 'opportunity' },
])
const priorityOptions = computed(() => [
  { label: t('crm.task.priority1'), value: '1' },
  { label: t('crm.task.priority2'), value: '2' },
  { label: t('crm.task.priority3'), value: '3' },
  { label: t('crm.task.priority4'), value: '4' },
  { label: t('crm.task.priority5'), value: '5' },
])
const formPriorityValue = computed({
  get: () => String(form.priority),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    form.priority = next == null ? 3 : Number(next)
  },
})
const taskStatusOptions = computed(() => [
  { label: t('crm.task.open'), value: String(CrmTaskStatus.Open) },
  { label: t('crm.task.inProgress'), value: String(CrmTaskStatus.InProgress) },
  { label: t('crm.task.completed'), value: String(CrmTaskStatus.Completed) },
  { label: t('crm.task.cancelled'), value: String(CrmTaskStatus.Cancelled) },
])
const formStatusValue = computed({
  get: () => String(form.status),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    form.status = next == null ? CrmTaskStatus.Open : Number(next) as CrmTaskStatus
  },
})
function toLocalDateTimeKey(value: number): string {
  const date = new Date(value)
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}
// XDatePicker 用时间戳收发；form.dueAt 仍是提交逻辑使用的 'YYYY-MM-DDTHH:mm'。
const dueAtValue = computed({
  get: () => form.dueAt ? new Date(form.dueAt).getTime() : null,
  set: (value: number | null) => {
    form.dueAt = value == null ? '' : toLocalDateTimeKey(value)
  },
})

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

function relatedTypeLabel(type?: string | null) {
  switch (type) {
    case 'customer': return t('crm.task.customer')
    case 'contact': return t('crm.task.contact')
    case 'lead': return t('crm.task.lead')
    case 'opportunity': return t('crm.task.opportunity')
    default: return '—'
  }
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
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmTaskListItemDto) {
  if (!canUpdate.value)
    return
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
  if (!canUpdate.value || row.status === status)
    return
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
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.task.confirmDelete', { name: row.title })))
    return
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
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.task.create') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="toolbar" :aria-label="t('crm.task.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.task.search') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput
            v-model:value="keyword"
            :placeholder="t('crm.task.search')"
            @enter="search"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.task.status') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XSelect
            v-model:value="statusFilterValue"
            :options="statusFilterOptions"
            :aria-label="t('crm.task.status')"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <span class="overdue-filter">
        <XhCheckbox v-model:checked="overdueOnly" :aria-label="t('crm.task.overdueOnly')" />
        <span>{{ t('crm.task.overdueOnly') }}</span>
      </span>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.task.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.task.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.task.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.task.empty') }}
      </div>
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
            <td>{{ relatedTypeLabel(row.relatedEntityType) }}</td>
            <td>{{ row.relatedEntityId || '—' }}</td>
            <td>{{ row.dueAt ? new Date(row.dueAt).toLocaleString() : '—' }}</td>
            <td><span class="status-pill" :data-status="row.status">{{ statusLabel(row.status) }}</span></td>
            <td>{{ priorityLabel(row.priority) }}</td>
            <td class="actions">
              <XhButton v-if="canUpdate" variant="outline" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.task.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton
                v-if="canUpdate && row.status === CrmTaskStatus.Open"
                variant="outline"
                @click="changeStatus(row, CrmTaskStatus.InProgress)"
              >
                <XhButtonLabel>{{ t('crm.task.inProgress') }}</XhButtonLabel>
              </XhButton>
              <XhButton
                v-if="canUpdate && row.status !== CrmTaskStatus.Completed && row.status !== CrmTaskStatus.Cancelled"
                variant="outline"
                @click="changeStatus(row, CrmTaskStatus.Completed)"
              >
                <XhButtonLabel>{{ t('crm.task.completed') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete" variant="outline" tone="danger" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.task.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.task.total', { count: totalCount }) }} · {{ t('crm.task.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.task.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.task.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.task.editTitle') : t('crm.task.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.task.editTitle') : t('crm.task.newTitle') }}</h2>
          <XhButton variant="subtle" size="sm" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.titleField') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.title" required :max-length="200" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.relatedType') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="form.relatedEntityType" :options="relatedTypeOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.relatedId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.relatedEntityId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.dueAt') }}</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="dueAtValue" show-time />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.priority') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="formPriorityValue" :options="priorityOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.task.assignee') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.assigneeUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot v-if="editingId">
            <XhFieldLabel>{{ t('crm.task.status') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="formStatusValue" :options="taskStatusOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="wide">
            <XhFieldLabel>{{ t('crm.task.descriptionField') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.description" type="textarea" :rows="3" />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.task.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.task.saving') : t('crm.task.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-task-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 100%;
  padding: 20px;
  color: hsl(var(--foreground));
}
.page-header,
.toolbar,
.pagination,
.editor header,
.editor footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.page-header h1,
.editor h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 650;
}
.page-header p {
  margin: 5px 0 0;
  color: hsl(var(--muted-foreground));
  font-size: 12px;
}
.toolbar {
  justify-content: flex-start;
  flex-wrap: wrap;
}
.toolbar-field {
  min-width: 240px;
}
.overdue-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}
.table-wrap {
  overflow: auto;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--card));
}
table {
  width: 100%;
  border-collapse: collapse;
  min-width: 900px;
  text-align: left;
  font-size: 13px;
}
th,
td {
  padding: 11px 12px;
  border-bottom: 1px solid hsl(var(--border));
  vertical-align: middle;
}
th {
  color: hsl(var(--muted-foreground));
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
.status-pill {
  display: inline-block;
  padding: 3px 8px;
  border-radius: var(--radius-full);
  background: hsl(var(--muted));
  font-size: 11px;
  white-space: nowrap;
}
.status-pill[data-status='2'] {
  color: hsl(var(--success));
}
.status-pill[data-status='3'] {
  color: hsl(var(--destructive));
}
.empty-state {
  padding: 44px 16px;
  text-align: center;
  color: hsl(var(--muted-foreground));
}
.pagination {
  color: hsl(var(--muted-foreground));
  font-size: 12px;
}
.pagination div {
  display: flex;
  gap: 6px;
}
.modal-backdrop {
  position: fixed;
  z-index: 1000;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 20px;
  background: hsl(var(--overlay));
}
.editor {
  width: min(760px, 100%);
  max-height: min(90vh, 900px);
  overflow: auto;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  padding: 20px;
  background: hsl(var(--card));
}
.editor form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 13px;
  margin-top: 18px;
}
.editor .wide,
.editor form footer {
  grid-column: 1 / -1;
}
.editor form footer {
  justify-content: flex-end;
}
@media (max-width: 680px) {
  .crm-task-page {
    padding: 12px;
  }
  .editor form {
    grid-template-columns: 1fr;
  }
  .editor .wide,
  .editor form footer {
    grid-column: auto;
  }
}
</style>
