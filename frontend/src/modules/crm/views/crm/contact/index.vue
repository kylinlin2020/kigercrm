<script setup lang="ts">
import type { CrmContactCreateDto, CrmContactListItemDto, CrmContactUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhCheckbox, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmContactApi } from '@/api'
import { XInput } from '~/components'
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
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmContactListItemDto) {
  if (!canUpdate.value)
    return
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
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.contact.confirmDelete', { name: row.fullName })))
    return
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
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.contact.create') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="toolbar" :aria-label="t('crm.contact.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.contact.search') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="keyword" :placeholder="t('crm.contact.search')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.contact.customerId') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="customerIdFilter" inputmode="numeric" :placeholder="t('crm.contact.customerId')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.contact.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.contact.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.contact.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.contact.empty') }}
      </div>
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
              <XhButton v-if="canUpdate" variant="subtle" size="sm" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.contact.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete" variant="subtle" tone="danger" size="sm" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.contact.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.contact.total', { count: totalCount }) }} · {{ t('crm.contact.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.contact.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" size="sm" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.contact.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.contact.editTitle') : t('crm.contact.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.contact.editTitle') : t('crm.contact.newTitle') }}</h2>
          <XhButton icon-only variant="ghost" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            ×
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.customerId') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.customerId" inputmode="numeric" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.firstName') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.firstName" :max-length="100" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.lastName') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.lastName" :max-length="100" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.jobTitle') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.jobTitle" :max-length="160" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.email') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.email" type="email" :max-length="254" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.phone') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.phone" :max-length="40" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.whatsApp') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.whatsApp" :max-length="40" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.contact.ownerId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.ownerUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="checkbox-field">
            <XhCheckbox v-model:checked="form.isPrimary">
              {{ t('crm.contact.primary') }}
            </XhCheckbox>
          </XhFieldRoot>
          <XhFieldRoot class="wide">
            <XhFieldLabel>{{ t('crm.contact.notes') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.notes" type="textarea" :rows="3" />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.contact.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.contact.saving') : t('crm.contact.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-contact-page {
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
.checkbox-field {
  align-self: center;
}
.editor .wide,
.editor form footer {
  grid-column: 1 / -1;
}
.editor form footer {
  justify-content: flex-end;
}
@media (max-width: 680px) {
  .crm-contact-page {
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
