<script setup lang="ts">
import type { CrmLeadCreateDto, CrmLeadDetailDto, CrmLeadListItemDto, CrmLeadUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmLeadStatus, crmLeadApi } from '@/api'
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

function statusLabel(status: CrmLeadStatus | number) {
  switch (status) {
    case CrmLeadStatus.Contacted: return t('crm.lead.contacted')
    case CrmLeadStatus.Qualified: return t('crm.lead.qualified')
    case CrmLeadStatus.Disqualified: return t('crm.lead.disqualified')
    case CrmLeadStatus.Converted: return t('crm.lead.converted')
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
  if (!canCreate.value) return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmLeadListItemDto) {
  if (!canUpdate.value) return
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
    if (duplicateCandidates.value.length > 0) return
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
  if (!canConvert.value || row.status === CrmLeadStatus.Converted) return
  if (!window.confirm(t('crm.lead.confirmConvert', { name: row.companyName }))) return
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
  if (!canDelete.value) return
  if (!window.confirm(t('crm.lead.confirmDelete', { name: row.companyName }))) return
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
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">
        {{ t('crm.lead.create') }}
      </button>
    </header>

    <section class="toolbar"  :aria-label="t('crm.lead.title')">
      <input
        v-model="keyword"
         :aria-label="t('crm.lead.searchPlaceholder')"
        :placeholder="t('crm.lead.searchPlaceholder')"
        @keydown.enter="search"
      >
      <select v-model="statusFilter"  :aria-label="t('crm.lead.status')">
        <option value="">{{ t('crm.lead.allStatuses') }}</option>
        <option :value="CrmLeadStatus.New">{{ t('crm.lead.new') }}</option>
        <option :value="CrmLeadStatus.Contacted">{{ t('crm.lead.contacted') }}</option>
        <option :value="CrmLeadStatus.Qualified">{{ t('crm.lead.qualified') }}</option>
        <option :value="CrmLeadStatus.Disqualified">{{ t('crm.lead.disqualified') }}</option>
        <option :value="CrmLeadStatus.Converted">{{ t('crm.lead.converted') }}</option>
      </select>
      <button type="button" @click="search">{{ t('crm.lead.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.lead.refresh') }}</button>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.lead.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.lead.empty') }}</div>
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
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.lead.edit') }}</button>
              <button v-if="canConvert && row.status !== CrmLeadStatus.Converted" type="button" @click="convert(row)">{{ t('crm.lead.convert') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.lead.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.lead.total', { count: totalCount }) }} · {{ t('crm.lead.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.lead.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.lead.next') }}</button>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.lead.editTitle') : t('crm.lead.createTitle')">
        <header>
          <div>
            <h2>{{ editingId ? t('crm.lead.editTitle') : t('crm.lead.createTitle') }}</h2>
            <p>{{ t('crm.lead.duplicateTip') }}</p>
          </div>
          <button type="button"  :aria-label="t('common.actions.close')" @click="editorOpen = false">×</button>
        </header>
        <form @submit.prevent="save">
          <label>{{ t('crm.lead.companyRequired') }}<input v-model="form.companyName" required maxlength="200"></label>
          <label>{{ t('crm.lead.contact') }}<input v-model="form.contactName" maxlength="200"></label>
          <label>{{ t('crm.lead.emailPhone') }}<input v-model="form.email" type="email" maxlength="254"></label>
          <label>{{ t('crm.lead.phone') }}<input v-model="form.phone" maxlength="40"></label>
          <label>{{ t('crm.lead.website') }}<input v-model="form.website" type="url" maxlength="500"></label>
          <label>{{ t('crm.lead.countryRegion') }}<input v-model="form.countryRegion" maxlength="100"></label>
          <label>{{ t('crm.lead.source') }}<input v-model="form.source" maxlength="80"></label>
          <label>{{ t('crm.lead.sourceReference') }}<input v-model="form.sourceReference" maxlength="500"></label>
          <label>{{ t('crm.lead.ownerId') }}<input v-model="form.ownerUserId" inputmode="numeric"></label>
          <label v-if="editingId">{{ t('crm.lead.status') }}
            <select v-model.number="form.status">
              <option :value="CrmLeadStatus.New">{{ t('crm.lead.new') }}</option>
              <option :value="CrmLeadStatus.Contacted">{{ t('crm.lead.contacted') }}</option>
              <option :value="CrmLeadStatus.Qualified">{{ t('crm.lead.qualified') }}</option>
              <option :value="CrmLeadStatus.Disqualified">{{ t('crm.lead.disqualified') }}</option>
            </select>
          </label>
          <label class="wide">{{ t('crm.lead.notes') }}<textarea v-model="form.notes" rows="3" /></label>

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
            <button type="button" @click="editorOpen = false">{{ t('crm.lead.cancel') }}</button>
            <button class="primary-button" type="submit" :disabled="submitting">
              {{ submitting ? t('crm.lead.saving') : duplicateCandidates.length ? t('crm.lead.duplicateContinue') : t('crm.lead.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-lead-page { display: flex; flex-direction: column; gap: 16px; min-height: 100%; padding: 20px; color: hsl(var(--foreground)); }
.page-header, .toolbar, .pagination, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.page-header h1, .editor h2 { margin: 0; font-size: 20px; font-weight: 650; }
.page-header p, .editor header p { margin: 5px 0 0; color: hsl(var(--muted-foreground)); font-size: 12px; }
button, input, select, textarea { font: inherit; }
button { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 7px 11px; background: hsl(var(--card)); cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.primary-button { border-color: hsl(var(--primary)); background: hsl(var(--primary)); color: hsl(var(--primary-foreground)); }
.toolbar { justify-content: flex-start; flex-wrap: wrap; }
.toolbar input { min-width: 260px; }
.toolbar input, .toolbar select, .editor input, .editor select, .editor textarea { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 8px 10px; background: hsl(var(--card)); color: inherit; }
.table-wrap { overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 10px; background: hsl(var(--card)); }
table { width: 100%; border-collapse: collapse; min-width: 880px; text-align: left; font-size: 13px; }
th, td { padding: 11px 12px; border-bottom: 1px solid hsl(var(--border)); vertical-align: middle; }
th { color: hsl(var(--muted-foreground)); font-size: 11px; font-weight: 600; white-space: nowrap; }
td small { display: block; margin-top: 3px; color: hsl(var(--muted-foreground)); }
.actions { white-space: nowrap; }
.actions button { margin-right: 5px; padding: 5px 8px; font-size: 11px; }
.danger-action { color: var(--xh-color-danger-600); }
.status-pill { display: inline-block; padding: 3px 8px; border-radius: 999px; background: hsl(var(--muted)); font-size: 11px; white-space: nowrap; }
.status-pill[data-status="2"] { color: var(--xh-color-success-600); }
.status-pill[data-status="3"] { color: var(--xh-color-danger-600); }
.status-pill[data-status="4"] { color: var(--xh-color-info-600); }
.empty-state { padding: 44px 16px; text-align: center; color: hsl(var(--muted-foreground)); }
.pagination { color: hsl(var(--muted-foreground)); font-size: 12px; }
.pagination div { display: flex; gap: 6px; }
.modal-backdrop { position: fixed; z-index: 1000; inset: 0; display: grid; place-items: center; padding: 20px; background: rgb(0 0 0 / 42%); }
.editor { width: min(760px, 100%); max-height: min(90vh, 900px); overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 12px; padding: 20px; background: hsl(var(--card)); box-shadow: 0 20px 70px rgb(0 0 0 / 20%); }
.editor form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 13px; margin-top: 18px; }
.editor label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: hsl(var(--muted-foreground)); }
.editor label input, .editor label select, .editor label textarea { width: 100%; box-sizing: border-box; color: hsl(var(--foreground)); }
.editor .wide, .duplicate-notice, .editor form footer { grid-column: 1 / -1; }
.duplicate-notice { padding: 12px; border: 1px solid var(--xh-color-warning-500); border-radius: 8px; background: color-mix(in srgb, var(--xh-color-warning-500) 8%, transparent); font-size: 12px; }
.duplicate-notice ul { margin: 8px 0; padding-left: 18px; }
.duplicate-notice small { color: hsl(var(--muted-foreground)); }
.editor form footer { justify-content: flex-end; }
@media (max-width: 680px) {
  .crm-lead-page { padding: 12px; }
  .editor form { grid-template-columns: 1fr; }
  .editor .wide, .duplicate-notice, .editor form footer { grid-column: auto; }
}
</style>
