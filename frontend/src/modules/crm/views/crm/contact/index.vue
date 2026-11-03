<script setup lang="ts">
import type { CrmContactCreateDto, CrmContactListItemDto, CrmContactUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmContactApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmContactPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const customerIdFilter = ref('')
const rows = ref<CrmContactListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive({
  customerId: '',
  firstName: '',
  lastName: '',
  jobTitle: '',
  email: '',
  phone: '',
  whatsApp: '',
  ownerUserId: '',
  isPrimary: false,
  notes: '',
})

const canCreate = computed(() => hasPermission('crm_contacts.create'))
const canUpdate = computed(() => hasPermission('crm_contacts.update'))
const canDelete = computed(() => hasPermission('crm_contacts.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function resetForm() {
  Object.assign(form, {
    customerId: customerIdFilter.value.trim(),
    firstName: '',
    lastName: '',
    jobTitle: '',
    email: '',
    phone: '',
    whatsApp: '',
    ownerUserId: '',
    isPrimary: false,
    notes: '',
  })
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmContactApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      customerId: customerIdFilter.value.trim() || null,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.contact.loadError'))
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

async function openEdit(row: CrmContactListItemDto) {
  if (!canUpdate.value) return
  loading.value = true
  try {
    const detail = await crmContactApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.contact.notFound'))
      return
    }
    resetForm()
    editingId.value = detail.basicId
    Object.assign(form, {
      customerId: detail.customerId,
      firstName: detail.firstName,
      lastName: detail.lastName,
      jobTitle: detail.jobTitle ?? '',
      email: detail.email ?? '',
      phone: detail.phone ?? '',
      whatsApp: detail.whatsApp ?? '',
      ownerUserId: detail.ownerUserId ?? '',
      isPrimary: detail.isPrimary,
      notes: detail.notes ?? '',
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.contact.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function save() {
  if (!form.customerId.trim() || !form.firstName.trim() || !form.lastName.trim()) {
    toast.warning(t('crm.contact.requiredFields'))
    return
  }
  submitting.value = true
  try {
    const common = {
      customerId: form.customerId.trim(),
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      jobTitle: form.jobTitle || null,
      email: form.email.trim() || null,
      phone: form.phone.trim() || null,
      whatsApp: form.whatsApp.trim() || null,
      ownerUserId: form.ownerUserId.trim() || null,
      isPrimary: form.isPrimary,
      notes: form.notes || null,
    }
    if (editingId.value) {
      const input: CrmContactUpdateDto = { ...common, basicId: editingId.value }
      await crmContactApi.update(input)
      toast.success(t('crm.contact.updateSuccess'))
    }
    else {
      const input: CrmContactCreateDto = common
      await crmContactApi.create(input)
      toast.success(t('crm.contact.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.contact.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function remove(row: CrmContactListItemDto) {
  if (!canDelete.value) return
  if (!window.confirm(t('crm.contact.confirmDelete', { name: row.fullName }))) return
  try {
    await crmContactApi.delete(row.basicId)
    toast.success(t('crm.contact.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.contact.deleteError'))
  }
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-contact-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.contact.title') }}</h1>
        <p>{{ t('crm.contact.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">
        {{ t('crm.contact.create') }}
      </button>
    </header>

    <section class="toolbar" :aria-label="t('crm.contact.title')">
      <input v-model="keyword" :aria-label="t('crm.contact.search')" :placeholder="t('crm.contact.search')" @keydown.enter="search">
      <input v-model="customerIdFilter" :aria-label="t('crm.contact.customerId')" :placeholder="t('crm.contact.customerId')" inputmode="numeric" @keydown.enter="search">
      <button type="button" @click="search">{{ t('crm.contact.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.contact.refresh') }}</button>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.contact.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.contact.empty') }}</div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.contact.name') }}</th>
            <th>{{ t('crm.contact.customerId') }}</th>
            <th>{{ t('crm.contact.jobTitle') }}</th>
            <th>{{ t('crm.contact.email') }}</th>
            <th>{{ t('crm.contact.phone') }}</th>
            <th>{{ t('crm.contact.primary') }}</th>
            <th>{{ t('crm.contact.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td><strong>{{ row.fullName }}</strong></td>
            <td>{{ row.customerId }}</td>
            <td>{{ row.jobTitle || '—' }}</td>
            <td>{{ row.email || '—' }}</td>
            <td>{{ row.phone || '—' }}</td>
            <td>{{ row.isPrimary ? t('crm.contact.yes') : '—' }}</td>
            <td class="actions">
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.contact.edit') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.contact.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.contact.total', { count: totalCount }) }} · {{ t('crm.contact.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.contact.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.contact.next') }}</button>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.contact.editTitle') : t('crm.contact.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.contact.editTitle') : t('crm.contact.newTitle') }}</h2>
          <button type="button" :aria-label="t('common.actions.close')" @click="editorOpen = false">×</button>
        </header>
        <form @submit.prevent="save">
          <label>{{ t('crm.contact.customerId') }} *<input v-model="form.customerId" required inputmode="numeric"></label>
          <label>{{ t('crm.contact.firstName') }} *<input v-model="form.firstName" required maxlength="100"></label>
          <label>{{ t('crm.contact.lastName') }} *<input v-model="form.lastName" required maxlength="100"></label>
          <label>{{ t('crm.contact.jobTitle') }}<input v-model="form.jobTitle" maxlength="160"></label>
          <label>{{ t('crm.contact.email') }}<input v-model="form.email" type="email" maxlength="254"></label>
          <label>{{ t('crm.contact.phone') }}<input v-model="form.phone" maxlength="40"></label>
          <label>{{ t('crm.contact.whatsApp') }}<input v-model="form.whatsApp" maxlength="40"></label>
          <label>{{ t('crm.contact.ownerId') }}<input v-model="form.ownerUserId" inputmode="numeric"></label>
          <label class="checkbox-label"><input v-model="form.isPrimary" type="checkbox"> {{ t('crm.contact.primary') }}</label>
          <label class="wide">{{ t('crm.contact.notes') }}<textarea v-model="form.notes" rows="3" /></label>
          <footer>
            <button type="button" @click="editorOpen = false">{{ t('crm.contact.cancel') }}</button>
            <button class="primary-button" type="submit" :disabled="submitting">{{ submitting ? t('crm.contact.saving') : t('crm.contact.save') }}</button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-contact-page { display: flex; flex-direction: column; gap: 16px; min-height: 100%; padding: 20px; color: hsl(var(--foreground)); }
.page-header, .toolbar, .pagination, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.page-header h1, .editor h2 { margin: 0; font-size: 20px; font-weight: 650; }
.page-header p { margin: 5px 0 0; color: hsl(var(--muted-foreground)); font-size: 12px; }
button, input, textarea { font: inherit; }
button { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 7px 11px; background: hsl(var(--card)); cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.primary-button { border-color: hsl(var(--primary)); background: hsl(var(--primary)); color: hsl(var(--primary-foreground)); }
.toolbar { justify-content: flex-start; flex-wrap: wrap; }
.toolbar input, .editor input, .editor textarea { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 8px 10px; background: hsl(var(--card)); color: inherit; }
.table-wrap { overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 10px; background: hsl(var(--card)); }
table { width: 100%; border-collapse: collapse; min-width: 880px; text-align: left; font-size: 13px; }
th, td { padding: 11px 12px; border-bottom: 1px solid hsl(var(--border)); vertical-align: middle; }
th { color: hsl(var(--muted-foreground)); font-size: 11px; font-weight: 600; white-space: nowrap; }
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
.editor .checkbox-label { flex-direction: row; align-items: center; }
.editor .checkbox-label input { width: auto; }
.editor form footer { justify-content: flex-end; }
@media (max-width: 680px) {
  .crm-contact-page { padding: 12px; }
  .editor form { grid-template-columns: 1fr; }
  .editor .wide, .editor form footer { grid-column: auto; }
}
</style>
