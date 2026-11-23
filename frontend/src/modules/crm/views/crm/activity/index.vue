<script setup lang="ts">
import type { CrmActivityCreateDto, CrmActivityListItemDto, CrmActivityUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmActivityApi, CrmActivityType } from '@/api'
import { XDatePicker, XInput, XSelect } from '~/components'
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
  occurredAt: Date.now(),
  ownerUserId: '',
  externalSystem: '',
  externalId: '',
})

const canCreate = computed(() => hasPermission('crm_activities.create'))
const canUpdate = computed(() => hasPermission('crm_activities.update'))
const canDelete = computed(() => hasPermission('crm_activities.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

/** XSelect 以字符串传值，这里把数字枚举还原，保持与接口 DTO 一致 */
const activityTypeFilterModel = computed({
  get: () => activityTypeFilter.value as string | number,
  set: (value: string | number | null) => {
    activityTypeFilter.value = value === '' || value == null ? '' : Number(value) as CrmActivityType
  },
})
const activityTypeModel = computed({
  get: () => form.activityType as string | number,
  set: (value: string | number | null) => {
    form.activityType = Number(value) as CrmActivityType
  },
})

const relationFilterOptions = computed(() => [
  { label: t('crm.activity.allRelations'), value: '' },
  { label: t('crm.activity.customer'), value: 'customer' },
  { label: t('crm.activity.contact'), value: 'contact' },
  { label: t('crm.activity.lead'), value: 'lead' },
  { label: t('crm.activity.opportunity'), value: 'opportunity' },
])
const relationOptions = computed(() => [
  { label: t('crm.activity.customer'), value: 'customer' },
  { label: t('crm.activity.contact'), value: 'contact' },
  { label: t('crm.activity.lead'), value: 'lead' },
  { label: t('crm.activity.opportunity'), value: 'opportunity' },
])
const activityTypeFilterOptions = computed(() => [
  { label: t('crm.activity.allTypes'), value: '' },
  { label: t('crm.activity.call'), value: CrmActivityType.Call },
  { label: t('crm.activity.email'), value: CrmActivityType.Email },
  { label: t('crm.activity.meeting'), value: CrmActivityType.Meeting },
  { label: t('crm.activity.note'), value: CrmActivityType.Note },
  { label: t('crm.activity.whatsApp'), value: CrmActivityType.WhatsApp },
  { label: t('crm.activity.other'), value: CrmActivityType.Other },
])
const activityTypeOptions = computed(() => [
  { label: t('crm.activity.call'), value: CrmActivityType.Call },
  { label: t('crm.activity.email'), value: CrmActivityType.Email },
  { label: t('crm.activity.meeting'), value: CrmActivityType.Meeting },
  { label: t('crm.activity.note'), value: CrmActivityType.Note },
  { label: t('crm.activity.whatsApp'), value: CrmActivityType.WhatsApp },
  { label: t('crm.activity.other'), value: CrmActivityType.Other },
])

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
    occurredAt: Date.now(),
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
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmActivityListItemDto) {
  if (!canUpdate.value)
    return
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
      occurredAt: new Date(detail.occurredAt).getTime(),
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
      occurredAt: new Date(form.occurredAt ?? Date.now()).toISOString(),
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
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.activity.confirmDelete', { name: row.subject })))
    return
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
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.activity.create') }}</XhButtonLabel>
      </XhButton>
    </header>
    <section class="toolbar" :aria-label="t('crm.activity.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.activity.search') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="keyword" :placeholder="t('crm.activity.search')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.activity.relatedType') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="relatedEntityType" :options="relationFilterOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.activity.type') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="activityTypeFilterModel" :options="activityTypeFilterOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.activity.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.activity.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>
    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.activity.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.activity.empty') }}
      </div>
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
              <XhButton v-if="canUpdate" variant="subtle" size="sm" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.activity.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete" variant="subtle" tone="danger" size="sm" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.activity.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <footer class="pagination">
      <span>{{ t('crm.activity.total', { count: totalCount }) }} · {{ t('crm.activity.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.activity.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.activity.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>
    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.activity.editTitle') : t('crm.activity.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.activity.editTitle') : t('crm.activity.newTitle') }}</h2>
          <XhButton icon-only variant="ghost" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            ×
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.relatedType') }} *</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="form.relatedEntityType" :options="relationOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.relatedId') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.relatedEntityId" inputmode="numeric" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.type') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="activityTypeModel" :options="activityTypeOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.subject') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.subject" :max-length="200" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.occurredAt') }} *</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="form.occurredAt" show-time />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.ownerId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.ownerUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.externalSystem') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.externalSystem" :max-length="80" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.activity.externalId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.externalId" :max-length="160" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="wide">
            <XhFieldLabel>{{ t('crm.activity.content') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.content" type="textarea" :rows="4" />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.activity.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.activity.saving') : t('crm.activity.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-activity-page {
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
  white-space: nowrap;
}
.actions > * + * {
  margin-left: 6px;
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
  box-shadow: 0 20px 70px hsl(var(--overlay));
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
  .crm-activity-page {
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
