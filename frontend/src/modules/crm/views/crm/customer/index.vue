<script setup lang="ts">
import type { CrmCustomerCreateDto, CrmCustomerListItemDto, CrmCustomerUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmCustomerApi } from '@/api'
import { XInput } from '~/components'
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
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmCustomerListItemDto) {
  if (!canUpdate.value)
    return
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
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.customer.confirmDelete', { name: row.name })))
    return
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
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.customer.create') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="toolbar" :aria-label="t('crm.customer.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.customer.search') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="keyword" :placeholder="t('crm.customer.search')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.customer.countryRegion') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="countryRegionFilter" :placeholder="t('crm.customer.countryRegion')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.customer.industry') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="industryFilter" :placeholder="t('crm.customer.industry')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.customer.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.customer.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.customer.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.customer.empty') }}
      </div>
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
              <XhButton v-if="canUpdate" variant="subtle" size="sm" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.customer.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete" variant="subtle" tone="danger" size="sm" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.customer.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.customer.total', { count: totalCount }) }} · {{ t('crm.customer.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.customer.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.customer.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.customer.editTitle') : t('crm.customer.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.customer.editTitle') : t('crm.customer.newTitle') }}</h2>
          <XhButton icon-only variant="ghost" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            ×
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.name') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.name" :max-length="200" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.legalName') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.legalName" :max-length="200" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.website') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.website" type="url" :max-length="500" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.countryRegion') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.countryRegion" :max-length="100" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.industry') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.industry" :max-length="100" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.source') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.source" :max-length="80" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.customer.ownerId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.ownerUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="wide">
            <XhFieldLabel>{{ t('crm.customer.notes') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.notes" type="textarea" :rows="3" />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.customer.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.customer.saving') : t('crm.customer.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-customer-page {
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
.toolbar input {
  min-width: 190px;
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
  min-width: 760px;
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
td a {
  color: hsl(var(--primary));
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
  .crm-customer-page {
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
