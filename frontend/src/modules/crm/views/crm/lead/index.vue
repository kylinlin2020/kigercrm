<script setup lang="ts">
import type { CrmLeadCreateDto, CrmLeadListItemDto, CrmLeadUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmLeadApi, CrmLeadStatus } from '@/api'
import { XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmLeadPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const statusFilter = ref<CrmLeadStatus | ''>('')
const rows = ref<CrmLeadListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const duplicateCandidates = ref<Awaited<ReturnType<typeof crmLeadApi.findPotentialDuplicates>>>([])
const duplicateChecked = ref(false)

const form = reactive({
  companyName: '',
  contactName: '',
  email: '',
  phone: '',
  website: '',
  countryRegion: '',
  source: '',
  sourceReference: '',
  ownerUserId: '',
  notes: '',
  status: CrmLeadStatus.New,
})

const canCreate = computed(() => hasPermission('crm_leads.create'))
const canUpdate = computed(() => hasPermission('crm_leads.update'))
const canDelete = computed(() => hasPermission('crm_leads.delete'))
const canConvert = computed(() => hasPermission('crm_leads.convert'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

const statusFilterOptions = computed(() => [
  { label: t('crm.lead.allStatuses'), value: '' },
  { label: t('crm.lead.new'), value: String(CrmLeadStatus.New) },
  { label: t('crm.lead.contacted'), value: String(CrmLeadStatus.Contacted) },
  { label: t('crm.lead.qualified'), value: String(CrmLeadStatus.Qualified) },
  { label: t('crm.lead.disqualified'), value: String(CrmLeadStatus.Disqualified) },
  { label: t('crm.lead.converted'), value: String(CrmLeadStatus.Converted) },
  { label: t('crm.lead.converting'), value: String(CrmLeadStatus.Converting) },
])
// XSelect 收发字符串选项；这里把值还原成 number | ''，保持原 v-model.number 的数字枚举语义。
const statusFilterValue = computed({
  get: () => String(statusFilter.value),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    statusFilter.value = next == null || next === '' ? '' : Number(next) as CrmLeadStatus
  },
})
const leadStatusOptions = computed(() => [
  { label: t('crm.lead.new'), value: String(CrmLeadStatus.New) },
  { label: t('crm.lead.contacted'), value: String(CrmLeadStatus.Contacted) },
  { label: t('crm.lead.qualified'), value: String(CrmLeadStatus.Qualified) },
  { label: t('crm.lead.disqualified'), value: String(CrmLeadStatus.Disqualified) },
])
const formStatusValue = computed({
  get: () => String(form.status),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    form.status = next == null ? CrmLeadStatus.New : Number(next) as CrmLeadStatus
  },
})

function statusLabel(status: CrmLeadStatus | number) {
  switch (status) {
    case CrmLeadStatus.Contacted: return t('crm.lead.contacted')
    case CrmLeadStatus.Qualified: return t('crm.lead.qualified')
    case CrmLeadStatus.Disqualified: return t('crm.lead.disqualified')
    case CrmLeadStatus.Converted: return t('crm.lead.converted')
    case CrmLeadStatus.Converting: return t('crm.lead.converting')
    default: return t('crm.lead.new')
  }
}

watch(form, () => {
  duplicateChecked.value = false
  duplicateCandidates.value = []
}, { deep: true })

function resetForm() {
  duplicateChecked.value = false
  Object.assign(form, {
    companyName: '',
    contactName: '',
    email: '',
    phone: '',
    website: '',
    countryRegion: '',
    source: '',
    sourceReference: '',
    ownerUserId: '',
    notes: '',
    status: CrmLeadStatus.New,
  })
  duplicateCandidates.value = []
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmLeadApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      status: statusFilter.value === '' ? null : statusFilter.value,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.lead.loadError'))
  }
  finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmLeadListItemDto) {
  if (!canUpdate.value)
    return
  loading.value = true
  try {
    const detail = await crmLeadApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.lead.notFound'))
      return
    }
    resetForm()
    editingId.value = detail.basicId
    Object.assign(form, {
      companyName: detail.companyName,
      contactName: detail.contactName ?? '',
      email: detail.email ?? '',
      phone: detail.phone ?? '',
      website: detail.website ?? '',
      countryRegion: detail.countryRegion ?? '',
      source: detail.source ?? '',
      sourceReference: detail.sourceReference ?? '',
      ownerUserId: detail.ownerUserId ?? '',
      notes: detail.notes ?? '',
      status: detail.status,
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.lead.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function checkDuplicates() {
  try {
    duplicateCandidates.value = await crmLeadApi.findPotentialDuplicates({
      excludeLeadId: editingId.value,
      companyName: form.companyName,
      email: form.email,
      phone: form.phone,
      website: form.website,
    })
  }
  catch (error) {
    // Duplicate lookup is advisory. A lookup outage must never block an otherwise valid Lead write.
    duplicateCandidates.value = []
    toast.warning((error as Error)?.message || t('crm.lead.duplicateUnavailable'))
  }
  finally {
    duplicateChecked.value = true
  }
}

async function save() {
  if (!form.companyName.trim()) {
    toast.warning(t('crm.lead.companyEmpty'))
    return
  }
  if (!duplicateChecked.value) {
    await checkDuplicates()
    if (duplicateCandidates.value.length > 0)
      return
  }
  submitting.value = true
  try {
    const common = {
      companyName: form.companyName.trim(),
      contactName: form.contactName || null,
      email: form.email || null,
      phone: form.phone || null,
      website: form.website || null,
      countryRegion: form.countryRegion || null,
      source: form.source || null,
      sourceReference: form.sourceReference || null,
      ownerUserId: form.ownerUserId || null,
      notes: form.notes || null,
    }
    if (editingId.value) {
      const input: CrmLeadUpdateDto = {
        ...common,
        basicId: editingId.value,
        status: form.status,
      }
      await crmLeadApi.update(input)
      toast.success(t('crm.lead.updateSuccess'))
    }
    else {
      const input: CrmLeadCreateDto = common
      await crmLeadApi.create(input)
      toast.success(t('crm.lead.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.lead.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function convert(row: CrmLeadListItemDto) {
  if (!canConvert.value || row.status === CrmLeadStatus.Converted)
    return
  if (!window.confirm(t('crm.lead.confirmConvert', { name: row.companyName })))
    return
  try {
    await crmLeadApi.convert(row.basicId)
    toast.success(t('crm.lead.convertSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.lead.convertError'))
  }
}

async function remove(row: CrmLeadListItemDto) {
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.lead.confirmDelete', { name: row.companyName })))
    return
  try {
    await crmLeadApi.delete(row.basicId)
    toast.success(t('crm.lead.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.lead.deleteError'))
  }
}

function search() {
  pageIndex.value = 1
  void loadPage()
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-lead-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.lead.title') }}</h1>
        <p>{{ t('crm.lead.description') }}</p>
      </div>
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.lead.create') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="toolbar" :aria-label="t('crm.lead.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.lead.searchPlaceholder') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput
            v-model:value="keyword"
            :placeholder="t('crm.lead.searchPlaceholder')"
            @enter="search"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.lead.status') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XSelect
            v-model:value="statusFilterValue"
            :options="statusFilterOptions"
            :aria-label="t('crm.lead.status')"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.lead.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.lead.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.lead.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.lead.empty') }}
      </div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.lead.company') }}</th>
            <th>{{ t('crm.lead.contact') }}</th>
            <th>{{ t('crm.lead.emailPhone') }}</th>
            <th>{{ t('crm.lead.countryRegion') }}</th>
            <th>{{ t('crm.lead.source') }}</th>
            <th>{{ t('crm.lead.status') }}</th>
            <th>{{ t('crm.lead.score') }}</th>
            <th>{{ t('crm.lead.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td>
              <strong>{{ row.companyName }}</strong>
              <small>{{ row.website || '—' }}</small>
            </td>
            <td>{{ row.contactName || '—' }}</td>
            <td>
              <span>{{ row.email || '—' }}</span>
              <small>{{ row.phone || '' }}</small>
            </td>
            <td>{{ row.countryRegion || '—' }}</td>
            <td>{{ row.source || '—' }}</td>
            <td><span class="status-pill" :data-status="row.status">{{ statusLabel(row.status) }}</span></td>
            <td>{{ row.score }}</td>
            <td class="actions">
              <XhButton v-if="canUpdate && row.status !== CrmLeadStatus.Converting && row.status !== CrmLeadStatus.Converted" variant="outline" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.lead.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canConvert && ![CrmLeadStatus.Converted, CrmLeadStatus.Converting, CrmLeadStatus.Disqualified].includes(row.status)" variant="outline" @click="convert(row)">
                <XhButtonLabel>{{ t('crm.lead.convert') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete && row.status !== CrmLeadStatus.Converting && row.status !== CrmLeadStatus.Converted" variant="outline" tone="danger" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.lead.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.lead.total', { count: totalCount }) }} · {{ t('crm.lead.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.lead.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.lead.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.lead.editTitle') : t('crm.lead.createTitle')">
        <header>
          <div>
            <h2>{{ editingId ? t('crm.lead.editTitle') : t('crm.lead.createTitle') }}</h2>
            <p>{{ t('crm.lead.duplicateTip') }}</p>
          </div>
          <XhButton variant="subtle" size="sm" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.companyRequired') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.companyName" required :max-length="200" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.contact') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.contactName" :max-length="200" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.email') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.email" type="email" :max-length="254" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.phone') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.phone" :max-length="40" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.website') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.website" type="url" :max-length="500" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.countryRegion') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.countryRegion" :max-length="100" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.source') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.source" :max-length="80" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.sourceReference') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.sourceReference" :max-length="500" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.lead.ownerId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.ownerUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot v-if="editingId">
            <XhFieldLabel>{{ t('crm.lead.status') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="formStatusValue" :options="leadStatusOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="wide">
            <XhFieldLabel>{{ t('crm.lead.notes') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.notes" type="textarea" :rows="3" />
            </XhFieldControl>
          </XhFieldRoot>

          <aside v-if="duplicateCandidates.length" class="duplicate-notice">
            <strong>{{ t('crm.lead.duplicateFound', { count: duplicateCandidates.length }) }}</strong>
            <ul>
              <li v-for="item in duplicateCandidates" :key="item.candidate.basicId">
                {{ item.candidate.companyName }} · {{ item.candidate.email || item.candidate.phone || t('crm.lead.matchedInfo') }}
                （{{ item.score }} 分，{{ item.confidence }}）
              </li>
            </ul>
            <small>{{ t('crm.lead.duplicateHint') }}</small>
          </aside>

          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.lead.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.lead.saving') : duplicateCandidates.length ? t('crm.lead.duplicateContinue') : t('crm.lead.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-lead-page {
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
.page-header p,
.editor header p {
  margin: 5px 0 0;
  color: hsl(var(--muted-foreground));
  font-size: 12px;
}
.toolbar {
  justify-content: flex-start;
  flex-wrap: wrap;
}
.toolbar-field {
  min-width: 260px;
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
  min-width: 880px;
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
td small {
  display: block;
  margin-top: 3px;
  color: hsl(var(--muted-foreground));
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
.status-pill[data-status='4'] {
  color: hsl(var(--info));
}
.status-pill[data-status='5'] {
  color: hsl(var(--warning));
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
.duplicate-notice,
.editor form footer {
  grid-column: 1 / -1;
}
.duplicate-notice {
  padding: 12px;
  border: 1px solid hsl(var(--warning));
  border-radius: var(--radius);
  background: color-mix(in srgb, hsl(var(--warning)) 8%, transparent);
  font-size: 12px;
}
.duplicate-notice ul {
  margin: 8px 0;
  padding-left: 18px;
}
.duplicate-notice small {
  color: hsl(var(--muted-foreground));
}
.editor form footer {
  justify-content: flex-end;
}
@media (max-width: 680px) {
  .crm-lead-page {
    padding: 12px;
  }
  .editor form {
    grid-template-columns: 1fr;
  }
  .editor .wide,
  .duplicate-notice,
  .editor form footer {
    grid-column: auto;
  }
}
</style>
