<script setup lang="ts">
import type { CrmCustomerCreateDto, CrmCustomerListItemDto, CrmCustomerUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmCustomerApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmCustomerPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const countryRegionFilter = ref('')
const industryFilter = ref('')
const rows = ref<CrmCustomerListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive({
  name: '',
  legalName: '',
  website: '',
  countryRegion: '',
  industry: '',
  source: '',
  ownerUserId: '',
  notes: '',
})

const canCreate = computed(() => hasPermission('crm_customers.create'))
const canUpdate = computed(() => hasPermission('crm_customers.update'))
const canDelete = computed(() => hasPermission('crm_customers.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function resetForm() {
  Object.assign(form, {
    name: '',
    legalName: '',
    website: '',
    countryRegion: '',
    industry: '',
    source: '',
    ownerUserId: '',
    notes: '',
  })
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmCustomerApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      countryRegion: countryRegionFilter.value.trim() || null,
      industry: industryFilter.value.trim() || null,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customer.loadError'))
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

async function openEdit(row: CrmCustomerListItemDto) {
  if (!canUpdate.value) return
  loading.value = true
  try {
    const detail = await crmCustomerApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.customer.notFound'))
      return
    }
    resetForm()
    editingId.value = detail.basicId
    Object.assign(form, {
      name: detail.name,
      legalName: detail.legalName ?? '',
      website: detail.website ?? '',
      countryRegion: detail.countryRegion ?? '',
      industry: detail.industry ?? '',
      source: detail.source ?? '',
      ownerUserId: detail.ownerUserId ?? '',
      notes: detail.notes ?? '',
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customer.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function save() {
  if (!form.name.trim()) {
    toast.warning(t('crm.customer.nameRequired'))
    return
  }
  submitting.value = true
  try {
    const common = {
      name: form.name.trim(),
      legalName: form.legalName || null,
      website: form.website || null,
      countryRegion: form.countryRegion || null,
      industry: form.industry || null,
      source: form.source || null,
      ownerUserId: form.ownerUserId.trim() || null,
      notes: form.notes || null,
    }
    if (editingId.value) {
      const input: CrmCustomerUpdateDto = { ...common, basicId: editingId.value }
      await crmCustomerApi.update(input)
      toast.success(t('crm.customer.updateSuccess'))
    }
    else {
      const input: CrmCustomerCreateDto = common
      await crmCustomerApi.create(input)
      toast.success(t('crm.customer.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customer.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function remove(row: CrmCustomerListItemDto) {
  if (!canDelete.value) return
  if (!window.confirm(t('crm.customer.confirmDelete', { name: row.name }))) return
  try {
    await crmCustomerApi.delete(row.basicId)
    toast.success(t('crm.customer.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customer.deleteError'))
  }
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-customer-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.customer.title') }}</h1>
        <p>{{ t('crm.customer.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">
        {{ t('crm.customer.create') }}
      </button>
    </header>

    <section class="toolbar" :aria-label="t('crm.customer.title')">
      <input
        v-model="keyword"
        :aria-label="t('crm.customer.search')"
        :placeholder="t('crm.customer.search')"
        @keydown.enter="search"
      >
      <input
        v-model="countryRegionFilter"
        :aria-label="t('crm.customer.countryRegion')"
        :placeholder="t('crm.customer.countryRegion')"
        @keydown.enter="search"
      >
      <input
        v-model="industryFilter"
        :aria-label="t('crm.customer.industry')"
        :placeholder="t('crm.customer.industry')"
        @keydown.enter="search"
      >
      <button type="button" @click="search">{{ t('crm.customer.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.customer.refresh') }}</button>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.customer.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.customer.empty') }}</div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.customer.name') }}</th>
            <th>{{ t('crm.customer.website') }}</th>
            <th>{{ t('crm.customer.countryRegion') }}</th>
            <th>{{ t('crm.customer.industry') }}</th>
            <th>{{ t('crm.customer.created') }}</th>
            <th>{{ t('crm.customer.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td>
              <strong>{{ row.name }}</strong>
            </td>
            <td>
              <a v-if="row.website" :href="row.website" target="_blank" rel="noopener noreferrer">{{ row.website }}</a>
              <span v-else>—</span>
            </td>
            <td>{{ row.countryRegion || '—' }}</td>
            <td>{{ row.industry || '—' }}</td>
            <td>{{ new Date(row.createdTime).toLocaleDateString() }}</td>
            <td class="actions">
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.customer.edit') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.customer.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.customer.total', { count: totalCount }) }} · {{ t('crm.customer.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.customer.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.customer.next') }}</button>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.customer.editTitle') : t('crm.customer.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.customer.editTitle') : t('crm.customer.newTitle') }}</h2>
          <button type="button" :aria-label="t('common.actions.close')" @click="editorOpen = false">×</button>
        </header>
        <form @submit.prevent="save">
          <label>{{ t('crm.customer.name') }} *<input v-model="form.name" required maxlength="200"></label>
          <label>{{ t('crm.customer.legalName') }}<input v-model="form.legalName" maxlength="200"></label>
          <label>{{ t('crm.customer.website') }}<input v-model="form.website" type="url" maxlength="500"></label>
          <label>{{ t('crm.customer.countryRegion') }}<input v-model="form.countryRegion" maxlength="100"></label>
          <label>{{ t('crm.customer.industry') }}<input v-model="form.industry" maxlength="100"></label>
          <label>{{ t('crm.customer.source') }}<input v-model="form.source" maxlength="80"></label>
          <label>{{ t('crm.customer.ownerId') }}<input v-model="form.ownerUserId" inputmode="numeric"></label>
          <label class="wide">{{ t('crm.customer.notes') }}<textarea v-model="form.notes" rows="3" /></label>
          <footer>
            <button type="button" @click="editorOpen = false">{{ t('crm.customer.cancel') }}</button>
            <button class="primary-button" type="submit" :disabled="submitting">
              {{ submitting ? t('crm.customer.saving') : t('crm.customer.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-customer-page { display: flex; flex-direction: column; gap: 16px; min-height: 100%; padding: 20px; color: hsl(var(--foreground)); }
.page-header, .toolbar, .pagination, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.page-header h1, .editor h2 { margin: 0; font-size: 20px; font-weight: 650; }
.page-header p { margin: 5px 0 0; color: hsl(var(--muted-foreground)); font-size: 12px; }
button, input, textarea { font: inherit; }
button { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 7px 11px; background: hsl(var(--card)); cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.primary-button { border-color: hsl(var(--primary)); background: hsl(var(--primary)); color: hsl(var(--primary-foreground)); }
.toolbar { justify-content: flex-start; flex-wrap: wrap; }
.toolbar input { min-width: 190px; }
.toolbar input, .editor input, .editor textarea { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 8px 10px; background: hsl(var(--card)); color: inherit; }
.table-wrap { overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 10px; background: hsl(var(--card)); }
table { width: 100%; border-collapse: collapse; min-width: 760px; text-align: left; font-size: 13px; }
th, td { padding: 11px 12px; border-bottom: 1px solid hsl(var(--border)); vertical-align: middle; }
th { color: hsl(var(--muted-foreground)); font-size: 11px; font-weight: 600; white-space: nowrap; }
td small { display: block; margin-top: 3px; color: hsl(var(--muted-foreground)); }
td a { color: hsl(var(--primary)); }
.actions { white-space: nowrap; }
.actions button { margin-right: 5px; padding: 5px 8px; font-size: 11px; }
.danger-action { color: var(--xh-color-danger-600); }
.empty-state { padding: 44px 16px; text-align: center; color: hsl(var(--muted-foreground)); }
.pagination { color: hsl(var(--muted-foreground)); font-size: 12px; }
.pagination div { display: flex; gap: 6px; }
.modal-backdrop { position: fixed; z-index: 1000; inset: 0; display: grid; place-items: center; padding: 20px; background: rgb(0 0 0 / 42%); }
.editor { width: min(760px, 100%); max-height: min(90vh, 900px); overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 12px; padding: 20px; background: hsl(var(--card)); box-shadow: 0 20px 70px rgb(0 0 0 / 20%); }
.editor form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 13px; margin-top: 18px; }
.editor label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: hsl(var(--muted-foreground)); }
.editor label input, .editor label textarea { width: 100%; box-sizing: border-box; color: hsl(var(--foreground)); }
.editor .wide, .editor form footer { grid-column: 1 / -1; }
.editor form footer { justify-content: flex-end; }
@media (max-width: 680px) {
  .crm-customer-page { padding: 12px; }
  .editor form { grid-template-columns: 1fr; }
  .editor .wide, .editor form footer { grid-column: auto; }
}
</style>
