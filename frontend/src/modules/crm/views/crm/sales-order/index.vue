<script setup lang="ts">
import type {
  CrmContactListItemDto,
  CrmCustomerListItemDto,
  CrmOpportunityListItemDto,
  CrmSalesOrderCreateDto,
  CrmSalesOrderDetailDto,
  CrmSalesOrderLineCreateDto,
  CrmSalesOrderLineDto,
  CrmSalesOrderLineUpdateDto,
  CrmSalesOrderListItemDto,
} from '@/api'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  createPageRequest,
  crmContactApi,
  crmCustomerApi,
  crmOpportunityApi,
  crmSalesOrderApi,
  CrmSalesOrderStatus,
} from '@/api'
import { XDatePicker, XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmSalesOrderPage' })
const { t } = useI18n()
const { hasPermission } = usePermission()

type NumericLineField = 'quantity' | 'unitPrice' | 'discountRate' | 'taxRate'
type SalesOrderLineForm = Omit<CrmSalesOrderLineCreateDto, NumericLineField> & Record<NumericLineField, string>
type EditLineForm = Omit<CrmSalesOrderLineUpdateDto, NumericLineField> & Record<NumericLineField, string>

/** XDatePicker 收发本地毫秒时间戳，提交时还原成后端要的 YYYY-MM-DD。 */
function toDateString(value: number | null): string {
  if (value == null)
    return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return ''
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}
function fromDateString(value: string): number | null {
  if (!value)
    return null
  const [year, month, day] = value.split('-').map(Number)
  if (!year || !month || !day)
    return null
  const timestamp = new Date(year, month - 1, day).getTime()
  return Number.isNaN(timestamp) ? null : timestamp
}

const rows = ref<CrmSalesOrderListItemDto[]>([])
const lines = ref<CrmSalesOrderLineDto[]>([])
const loading = ref(false)
const keyword = ref('')
const status = ref<CrmSalesOrderStatus | ''>('')
const pageIndex = ref(1)
const totalCount = ref(0)
const pageSize = 20
const selected = ref<CrmSalesOrderDetailDto | CrmSalesOrderListItemDto | null>(null)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))
const canExecute = computed(() => hasPermission('crm_sales_orders.execute'))
const canUpdate = computed(() => hasPermission('crm_sales_orders.update'))

const canCreate = computed(() => hasPermission('crm_sales_orders.create'))
const customerOptions = ref<CrmCustomerListItemDto[]>([])
const contactOptions = ref<CrmContactListItemDto[]>([])
const opportunityOptions = ref<CrmOpportunityListItemDto[]>([])
const customerSearch = ref('')
const loadingRelations = ref(false)
const createOpen = ref(false)
const saving = ref(false)
const createIdempotencyKey = ref('')
const editOpen = ref(false)
const editSaving = ref(false)
const editForm = ref({
  id: '',
  customerId: '',
  contactId: '',
  opportunityId: '',
  orderDate: null as number | null,
  currency: 'USD',
  customerPurchaseOrderNumber: '',
  paymentTerms: '',
  deliveryTerms: '',
  termsAndConditions: '',
  notes: '',
})
const editLines = ref<EditLineForm[]>([])
const editQuoteLinked = ref(false)
let customerSearchRequest = 0
let relationRequest = 0
const form = ref({
  orderNumber: '',
  customerId: '',
  contactId: '',
  opportunityId: '',
  orderDate: fromDateString(new Date().toISOString().slice(0, 10)),
  currency: 'USD',
  customerPurchaseOrderNumber: '',
  paymentTerms: '',
  deliveryTerms: '',
  termsAndConditions: '',
  notes: '',
})

function label(s: number) {
  return t(`crm.salesOrder.status${s}`)
}

// XSelect 用字符串选项；这里把值还原成 number | ''，保持原数字枚举语义。
const statusOptions = computed(() => [
  { label: t('crm.salesOrder.allStatuses'), value: '' },
  ...[0, 1, 2, 3, 4, 5].map(s => ({ label: label(s), value: String(s) })),
])
const statusValue = computed({
  get: () => String(status.value),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    status.value = next == null || next === '' ? '' : Number(next) as CrmSalesOrderStatus
  },
})
const customerSelectOptions = computed(() => [
  { label: t('crm.salesOrder.selectCustomer'), value: '' },
  ...customerOptions.value.map(customer => ({
    label: `${customer.name} (#${customer.basicId})`,
    value: String(customer.basicId),
  })),
])
const customerIdValue = computed({
  get: () => form.value.customerId,
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    form.value.customerId = next == null ? '' : String(next)
    void loadCustomerRelations(form.value.customerId)
  },
})
const contactSelectOptions = computed(() => [
  { label: t('crm.salesOrder.noContact'), value: '' },
  ...contactOptions.value.map(contact => ({
    label: `${contact.fullName} (#${contact.basicId})`,
    value: String(contact.basicId),
  })),
])
const opportunitySelectOptions = computed(() => [
  { label: t('crm.salesOrder.noOpportunity'), value: '' },
  ...opportunityOptions.value.map(opportunity => ({
    label: `${opportunity.name} (#${opportunity.basicId})`,
    value: String(opportunity.basicId),
  })),
])

function newLine(): SalesOrderLineForm {
  return {
    productCode: '',
    description: '',
    quantity: '1',
    unit: '',
    unitPrice: '0',
    discountRate: '0',
    taxRate: '0',
    sortOrder: 0,
  }
}
const draftLines = ref<SalesOrderLineForm[]>([newLine()])
const previewTotal = computed(() =>
  draftLines.value.reduce((sum, line) => {
    const subtotal = Math.round(Number(line.quantity) * Number(line.unitPrice) * 100) / 100
    const discount = Math.round(subtotal * Number(line.discountRate)) / 100
    const tax = Math.round((subtotal - discount) * Number(line.taxRate)) / 100
    return sum + subtotal - discount + tax
  }, 0),
)
function addLine() {
  if (draftLines.value.length < 500)
    draftLines.value.push({ ...newLine(), sortOrder: draftLines.value.length })
}
function removeLine(index: number) {
  if (draftLines.value.length > 1)
    draftLines.value.splice(index, 1)
}
async function searchCustomers() {
  const requestId = ++customerSearchRequest
  try {
    const p = createPageRequest({ page: { pageIndex: 1, pageSize: 100 } })
    const r = await crmCustomerApi.page({ ...p, keyword: customerSearch.value.trim() || null })
    if (requestId === customerSearchRequest)
      customerOptions.value = r.items
  }
  catch (e) {
    if (requestId === customerSearchRequest)
      toast.danger((e as Error)?.message || t('crm.salesOrder.customerLoadError'))
  }
}
async function loadCustomerRelations(customerId: string) {
  const requestId = ++relationRequest
  contactOptions.value = []
  opportunityOptions.value = []
  form.value.contactId = ''
  form.value.opportunityId = ''
  if (!customerId) {
    loadingRelations.value = false
    return
  }
  loadingRelations.value = true
  try {
    const p = createPageRequest({ page: { pageIndex: 1, pageSize: 100 } })
    const [contacts, opportunities] = await Promise.all([
      crmContactApi.page({ ...p, customerId }),
      crmOpportunityApi.page({ ...p, customerId }),
    ])
    if (requestId === relationRequest) {
      contactOptions.value = contacts.items
      opportunityOptions.value = opportunities.items
    }
  }
  catch (e) {
    if (requestId === relationRequest)
      toast.danger((e as Error)?.message || t('crm.salesOrder.relatedLoadError'))
  }
  finally {
    if (requestId === relationRequest)
      loadingRelations.value = false
  }
}
function newIdempotencyKey() {
  return globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
}
async function openCreate() {
  createIdempotencyKey.value = newIdempotencyKey()
  form.value = {
    orderNumber: '',
    customerId: '',
    contactId: '',
    opportunityId: '',
    orderDate: fromDateString(new Date().toISOString().slice(0, 10)),
    currency: 'USD',
    customerPurchaseOrderNumber: '',
    paymentTerms: '',
    deliveryTerms: '',
    termsAndConditions: '',
    notes: '',
  }
  draftLines.value = [newLine()]
  contactOptions.value = []
  opportunityOptions.value = []
  customerSearch.value = ''
  createOpen.value = true
  await searchCustomers()
}
async function createOrder() {
  const validLine = (line: SalesOrderLineForm) =>
    line.description.trim()
    && Number.isFinite(Number(line.quantity))
    && Number(line.quantity) > 0
    && Math.abs(Number(line.quantity) * 10000 - Math.round(Number(line.quantity) * 10000)) < 1e-8
    && Number.isFinite(Number(line.unitPrice))
    && Number(line.unitPrice) >= 0
    && Number.isFinite(Number(line.discountRate))
    && Number(line.discountRate) >= 0
    && Number(line.discountRate) <= 100
    && Number.isFinite(Number(line.taxRate))
    && Number(line.taxRate) >= 0
    && Number(line.taxRate) <= 100
  if (
    !form.value.customerId.trim()
    || !form.value.orderDate
    || !/^[A-Z]{3}$/i.test(form.value.currency.trim())
    || draftLines.value.length === 0
    || draftLines.value.some(line => !validLine(line))
  ) {
    toast.warning(t('crm.salesOrder.createRequired'))
    return
  }
  saving.value = true
  try {
    const input: CrmSalesOrderCreateDto = {
      orderNumber: form.value.orderNumber.trim(),
      numberingIdempotencyKey: createIdempotencyKey.value,
      customerId: form.value.customerId.trim(),
      contactId: form.value.contactId.trim() || null,
      opportunityId: form.value.opportunityId.trim() || null,
      orderDate: `${toDateString(form.value.orderDate)}T00:00:00`,
      currency: form.value.currency.trim().toUpperCase(),
      customerPurchaseOrderNumber: form.value.customerPurchaseOrderNumber.trim() || null,
      paymentTerms: form.value.paymentTerms.trim() || null,
      deliveryTerms: form.value.deliveryTerms.trim() || null,
      termsAndConditions: form.value.termsAndConditions.trim() || null,
      notes: form.value.notes.trim() || null,
      lines: draftLines.value.map((line, index) => ({
        ...line,
        quantity: Number(line.quantity),
        unitPrice: Number(line.unitPrice),
        discountRate: Number(line.discountRate),
        taxRate: Number(line.taxRate),
        productCode: line.productCode?.trim() || null,
        description: line.description.trim(),
        unit: line.unit?.trim() || null,
        sortOrder: index,
      })),
    }
    await crmSalesOrderApi.create(input)
    toast.success(t('crm.salesOrder.created'))
    createOpen.value = false
    createIdempotencyKey.value = ''
    await loadPage()
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.createError'))
  }
  finally {
    saving.value = false
  }
}

function newEditLine(): EditLineForm {
  return {
    id: '0',
    productCode: '',
    description: '',
    quantity: '1',
    unit: '',
    unitPrice: '0',
    discountRate: '0',
    taxRate: '0',
    sortOrder: editLines.value.length,
  }
}
function addEditLine() {
  if (!editQuoteLinked.value && editLines.value.length < 500)
    editLines.value.push(newEditLine())
}
function removeEditLine(index: number) {
  if (editLines.value.length > 1 && !editLines.value[index]?.sourceQuoteLineId)
    editLines.value.splice(index, 1)
}
async function openEdit(row: CrmSalesOrderListItemDto) {
  try {
    const [detail, result] = await Promise.all([
      crmSalesOrderApi.detail(row.basicId),
      crmSalesOrderApi.lines({
        ...createPageRequest({ page: { pageIndex: 1, pageSize: 500 } }),
        salesOrderId: row.basicId,
      }),
    ])
    if (!detail || detail.status !== CrmSalesOrderStatus.Draft) {
      toast.warning(t('crm.salesOrder.editDraftOnly'))
      return
    }
    if (result.items.some(line => line.reservedQuantity > 0 || line.shippedQuantity > 0)) {
      toast.warning(t('crm.salesOrder.editFulfillmentLocked'))
      return
    }
    editQuoteLinked.value = !!detail.sourceQuoteId
    editForm.value = {
      id: String(detail.basicId),
      customerId: String(detail.customerId),
      contactId: detail.contactId ? String(detail.contactId) : '',
      opportunityId: detail.opportunityId ? String(detail.opportunityId) : '',
      orderDate: fromDateString(new Date(detail.orderDate).toISOString().slice(0, 10)),
      currency: detail.currency,
      customerPurchaseOrderNumber: detail.customerPurchaseOrderNumber || '',
      paymentTerms: detail.paymentTerms || '',
      deliveryTerms: detail.deliveryTerms || '',
      termsAndConditions: detail.termsAndConditions || '',
      notes: detail.notes || '',
    }
    editLines.value = result.items.map(line => ({
      id: line.basicId,
      sourceQuoteLineId: line.sourceQuoteLineId || null,
      productCode: line.productCode || '',
      description: line.description,
      quantity: String(line.quantity),
      unit: line.unit || '',
      unitPrice: String(line.unitPrice),
      discountRate: String(line.discountRate),
      taxRate: String(line.taxRate),
      sortOrder: line.sortOrder,
    }))
    editOpen.value = true
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.editLoadError'))
  }
}
async function saveEdit() {
  const valid
    = editLines.value.length > 0
      && editLines.value.every(
        line =>
          line.description.trim()
          && Number.isFinite(Number(line.quantity))
          && Number(line.quantity) > 0
          && Number.isFinite(Number(line.unitPrice))
          && Number(line.unitPrice) >= 0
          && Number(line.discountRate) >= 0
          && Number(line.discountRate) <= 100
          && Number(line.taxRate) >= 0
          && Number(line.taxRate) <= 100,
      )
  if (!valid || !/^[A-Z]{3}$/i.test(editForm.value.currency.trim())) {
    toast.warning(t('crm.salesOrder.createRequired'))
    return
  }
  editSaving.value = true
  try {
    await crmSalesOrderApi.updateDraft({
      id: editForm.value.id,
      customerId: editForm.value.customerId,
      contactId: editForm.value.contactId || null,
      opportunityId: editForm.value.opportunityId || null,
      orderDate: `${toDateString(editForm.value.orderDate)}T00:00:00`,
      currency: editForm.value.currency.trim().toUpperCase(),
      customerPurchaseOrderNumber: editForm.value.customerPurchaseOrderNumber.trim() || null,
      paymentTerms: editForm.value.paymentTerms.trim() || null,
      deliveryTerms: editForm.value.deliveryTerms.trim() || null,
      termsAndConditions: editForm.value.termsAndConditions.trim() || null,
      notes: editForm.value.notes.trim() || null,
      lines: editLines.value.map((line, index) => ({
        ...line,
        quantity: Number(line.quantity),
        unitPrice: Number(line.unitPrice),
        discountRate: Number(line.discountRate),
        taxRate: Number(line.taxRate),
        description: line.description.trim(),
        productCode: line.productCode?.trim() || null,
        unit: line.unit?.trim() || null,
        sortOrder: line.sourceQuoteLineId ? line.sortOrder : index,
      })),
    })
    toast.success(t('crm.salesOrder.editSaved'))
    editOpen.value = false
    selected.value = null
    await loadPage()
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.editError'))
  }
  finally {
    editSaving.value = false
  }
}

async function loadPage() {
  loading.value = true
  try {
    const p = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const r = await crmSalesOrderApi.page({
      ...p,
      keyword: keyword.value.trim() || null,
      status: status.value === '' ? null : status.value,
    })
    rows.value = r.items
    totalCount.value = r.page.totalCount
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.loadError'))
  }
  finally {
    loading.value = false
  }
}
async function openLines(row: CrmSalesOrderListItemDto) {
  selected.value = row
  lines.value = []
  try {
    const p = createPageRequest({ page: { pageIndex: 1, pageSize: 500 } })
    const [detail, r] = await Promise.all([
      crmSalesOrderApi.detail(row.basicId),
      crmSalesOrderApi.lines({ ...p, salesOrderId: row.basicId }),
    ])
    selected.value = detail || row
    lines.value = r.items
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.linesError'))
  }
}
async function changeStatus(row: CrmSalesOrderListItemDto, next: CrmSalesOrderStatus) {
  try {
    await crmSalesOrderApi.changeStatus({ id: row.basicId, status: next })
    toast.success(t('crm.salesOrder.statusSaved'))
    await loadPage()
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.salesOrder.statusError'))
  }
}
function resetAndLoad() {
  pageIndex.value = 1
  loadPage()
}
function previousPage() {
  pageIndex.value--
  loadPage()
}
function nextPage() {
  pageIndex.value++
  loadPage()
}
onMounted(loadPage)
</script>

<template>
  <main class="crm-workflow-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.salesOrder.title') }}</h1>
        <p>{{ t('crm.salesOrder.description') }}</p>
      </div>
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.salesOrder.create') }}</XhButtonLabel>
      </XhButton>
    </header>
    <section class="toolbar">
      <XhFieldRoot class="form-field">
        <XhFieldLabel>{{ t('crm.salesOrder.search') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="keyword" :placeholder="t('crm.salesOrder.search')" @enter="resetAndLoad" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="form-field">
        <XhFieldLabel>{{ t('crm.salesOrder.status') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="statusValue" :options="statusOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="resetAndLoad">
        <XhButtonLabel>{{ t('crm.salesOrder.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.salesOrder.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>
    <section class="table-wrap">
      <div v-if="loading && !rows.length" class="empty">
        {{ t('crm.salesOrder.loading') }}
      </div>
      <div v-else-if="!rows.length" class="empty">
        {{ t('crm.salesOrder.empty') }}
      </div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.salesOrder.number') }}</th>
            <th>{{ t('crm.salesOrder.customer') }}</th>
            <th>{{ t('crm.salesOrder.date') }}</th>
            <th>{{ t('crm.salesOrder.status') }}</th>
            <th>{{ t('crm.salesOrder.total') }}</th>
            <th>{{ t('crm.salesOrder.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td>{{ row.orderNumber }}</td>
            <td>{{ row.customerNameSnapshot }} (#{{ row.customerId }})</td>
            <td>{{ new Date(row.orderDate).toLocaleDateString() }}</td>
            <td>{{ label(row.status) }}</td>
            <td>{{ row.grandTotal.toLocaleString() }} {{ row.currency }}</td>
            <td class="actions">
              <XhButton variant="subtle" @click="openLines(row)">
                <XhButtonLabel>{{ t('crm.salesOrder.viewLines') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canUpdate && row.status === CrmSalesOrderStatus.Draft" variant="outline" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.salesOrder.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton
                v-if="canExecute && row.status === CrmSalesOrderStatus.Draft"
                variant="outline"
                @click="changeStatus(row, CrmSalesOrderStatus.Confirmed)"
              >
                <XhButtonLabel>{{ t('crm.salesOrder.confirm') }}</XhButtonLabel>
              </XhButton>
              <XhButton
                v-if="canExecute && row.status === CrmSalesOrderStatus.Confirmed"
                variant="outline"
                @click="changeStatus(row, CrmSalesOrderStatus.Processing)"
              >
                <XhButtonLabel>{{ t('crm.salesOrder.start') }}</XhButtonLabel>
              </XhButton>
              <XhButton
                v-if="
                  canExecute
                    && (row.status === CrmSalesOrderStatus.Draft || row.status === CrmSalesOrderStatus.Confirmed)
                "
                variant="outline"
                tone="danger"
                @click="changeStatus(row, CrmSalesOrderStatus.Cancelled)"
              >
                <XhButtonLabel>{{ t('crm.salesOrder.cancelOrder') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <footer class="pagination">
      <span>
        {{ t('crm.salesOrder.totalCount', { count: totalCount }) }} ·
        {{ t('crm.salesOrder.page', { current: pageIndex, total: totalPages }) }}
      </span>
      <div>
        <XhButton
          variant="subtle"
          :disabled="pageIndex <= 1 || loading"
          @click="previousPage"
        >
          <XhButtonLabel>{{ t('crm.salesOrder.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton
          variant="subtle"
          :disabled="pageIndex >= totalPages || loading"
          @click="nextPage"
        >
          <XhButtonLabel>{{ t('crm.salesOrder.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>
    <div v-if="createOpen" class="modal-backdrop" @click.self="createOpen = false">
      <section class="editor create-editor">
        <header>
          <h2>{{ t('crm.salesOrder.createTitle') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.salesOrder.cancelForm')" :disabled="saving" @click="createOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="createOrder">
          <div class="form-grid">
            <div class="customer-picker">
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.salesOrder.customerSearch') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput
                    v-model:value="customerSearch"
                    :placeholder="t('crm.salesOrder.customerSearch')"
                    @keydown.enter.prevent="searchCustomers"
                  />
                </XhFieldControl>
              </XhFieldRoot>
              <XhButton variant="subtle" @click="searchCustomers">
                <XhButtonLabel>{{ t('crm.salesOrder.searchCustomers') }}</XhButtonLabel>
              </XhButton>
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.salesOrder.customer') }} *</XhFieldLabel>
                <XhFieldControl>
                  <XSelect v-model:value="customerIdValue" :options="customerSelectOptions" required />
                </XhFieldControl>
              </XhFieldRoot>
            </div>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.contactId') }}</XhFieldLabel>
              <XhFieldControl>
                <XSelect
                  v-model:value="form.contactId"
                  :options="contactSelectOptions"
                  :disabled="!form.customerId || loadingRelations"
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.opportunityId') }}</XhFieldLabel>
              <XhFieldControl>
                <XSelect
                  v-model:value="form.opportunityId"
                  :options="opportunitySelectOptions"
                  :disabled="!form.customerId || loadingRelations"
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.date') }} *</XhFieldLabel>
              <XhFieldControl>
                <XDatePicker v-model:value="form.orderDate" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.currency') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.currency" :max-length="3" required />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.customerPo') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.customerPurchaseOrderNumber" :max-length="100" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.paymentTerms') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.paymentTerms" :max-length="500" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.deliveryTerms') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.deliveryTerms" :max-length="500" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field wide">
              <XhFieldLabel>Terms &amp; Conditions</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.termsAndConditions" type="textarea" :rows="4" :max-length="20000" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field wide">
              <XhFieldLabel>{{ t('crm.salesOrder.notes') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="form.notes" type="textarea" :rows="2" />
              </XhFieldControl>
            </XhFieldRoot>
          </div>
          <div class="line-heading">
            <h3>{{ t('crm.salesOrder.lines') }}</h3>
            <XhButton variant="outline" :disabled="draftLines.length >= 500" @click="addLine">
              <XhButtonLabel>{{ t('crm.salesOrder.addLine') }}</XhButtonLabel>
            </XhButton>
          </div>
          <div v-for="(line, index) in draftLines" :key="index" class="line-editor">
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.productCode') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.productCode" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field description-field">
              <XhFieldLabel>{{ t('crm.salesOrder.descriptionLine') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.description" :max-length="500" required />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.quantity') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.quantity" inputmode="numeric" required />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.unit') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.unit" :max-length="30" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.unitPrice') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.unitPrice" inputmode="numeric" required />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.discountRate') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.discountRate" inputmode="numeric" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.taxRate') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.taxRate" inputmode="numeric" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhButton
              class="remove-line"
              variant="subtle"
              tone="danger"
              :disabled="draftLines.length === 1"
              @click="removeLine(index)"
            >
              <XhButtonLabel>{{ t('crm.salesOrder.removeLine') }}</XhButtonLabel>
            </XhButton>
          </div>
          <div class="total-preview">
            {{ t('crm.salesOrder.estimatedTotal') }}:
            <strong>
              {{ previewTotal.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }}
              {{ form.currency.toUpperCase() }}
            </strong>
            <small>{{ t('crm.salesOrder.serverTotalNotice') }}</small>
          </div>
          <footer class="form-actions">
            <XhButton variant="subtle" :disabled="saving" @click="createOpen = false">
              <XhButtonLabel>{{ t('crm.salesOrder.cancelForm') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="saving">
              <XhButtonLabel>{{ saving ? t('crm.salesOrder.saving') : t('crm.salesOrder.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
    <div v-if="editOpen" class="modal-backdrop" @click.self="editOpen = false">
      <section class="editor create-editor">
        <header>
          <h2>{{ t('crm.salesOrder.editTitle') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.salesOrder.cancelForm')" :disabled="editSaving" @click="editOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="saveEdit">
          <div class="form-grid">
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.date') }} *</XhFieldLabel>
              <XhFieldControl>
                <XDatePicker v-model:value="editForm.orderDate" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.currency') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.currency" :max-length="3" required />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.customerPo') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.customerPurchaseOrderNumber" :max-length="100" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.paymentTerms') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.paymentTerms" :max-length="500" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.deliveryTerms') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.deliveryTerms" :max-length="500" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field wide">
              <XhFieldLabel>Terms &amp; Conditions</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.termsAndConditions" type="textarea" :rows="4" :max-length="20000" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field wide">
              <XhFieldLabel>{{ t('crm.salesOrder.notes') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="editForm.notes" type="textarea" :rows="2" />
              </XhFieldControl>
            </XhFieldRoot>
          </div>
          <div class="line-heading">
            <h3>{{ t('crm.salesOrder.lines') }}</h3>
            <small>
              {{
                editQuoteLinked ? t('crm.salesOrder.quoteOrderLinesLocked') : t('crm.salesOrder.editLineCountNotice')
              }}
            </small>
            <XhButton
              v-if="!editQuoteLinked"
              variant="outline"
              :disabled="editSaving || editLines.length >= 500"
              @click="addEditLine"
            >
              <XhButtonLabel>{{ t('crm.salesOrder.addLine') }}</XhButtonLabel>
            </XhButton>
          </div>
          <div v-for="(line, index) in editLines" :key="`${String(line.id)}-${index}`" class="line-editor">
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.productCode') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.productCode" :disabled="!!line.sourceQuoteLineId" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field description-field">
              <XhFieldLabel>{{ t('crm.salesOrder.descriptionLine') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput
                  v-model:value="line.description"
                  :disabled="!!line.sourceQuoteLineId"
                  required
                  :max-length="500"
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.quantity') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput
                  v-model:value="line.quantity"
                  :disabled="!!line.sourceQuoteLineId"
                  inputmode="numeric"
                  required
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.unit') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput v-model:value="line.unit" :disabled="!!line.sourceQuoteLineId" />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.unitPrice') }} *</XhFieldLabel>
              <XhFieldControl>
                <XInput
                  v-model:value="line.unitPrice"
                  :disabled="!!line.sourceQuoteLineId"
                  inputmode="numeric"
                  required
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.discountRate') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput
                  v-model:value="line.discountRate"
                  :disabled="!!line.sourceQuoteLineId"
                  inputmode="numeric"
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhFieldRoot class="form-field">
              <XhFieldLabel>{{ t('crm.salesOrder.taxRate') }}</XhFieldLabel>
              <XhFieldControl>
                <XInput
                  v-model:value="line.taxRate"
                  :disabled="!!line.sourceQuoteLineId"
                  inputmode="numeric"
                />
              </XhFieldControl>
            </XhFieldRoot>
            <XhButton
              class="remove-line"
              variant="subtle"
              tone="danger"
              :disabled="editSaving || !!line.sourceQuoteLineId || editLines.length <= 1"
              @click="removeEditLine(index)"
            >
              <XhButtonLabel>{{ line.sourceQuoteLineId ? t('crm.salesOrder.quoteLineLocked') : t('crm.salesOrder.removeLine') }}</XhButtonLabel>
            </XhButton>
          </div>
          <footer class="form-actions">
            <XhButton variant="subtle" :disabled="editSaving" @click="editOpen = false">
              <XhButtonLabel>{{ t('crm.salesOrder.cancelForm') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="editSaving">
              <XhButtonLabel>{{ editSaving ? t('crm.salesOrder.saving') : t('crm.salesOrder.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
    <div v-if="selected" class="modal-backdrop" @click.self="selected = null">
      <section class="editor">
        <header>
          <h2>{{ selected.orderNumber }} · {{ t('crm.salesOrder.lines') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.salesOrder.cancelForm')" @click="selected = null">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <p>{{ selected.customerNameSnapshot }} · {{ label(selected.status) }}</p>
        <div v-if="'subtotal' in selected" class="order-detail-grid">
          <span>{{ t('crm.salesOrder.customerPo') }}: {{ selected.customerPurchaseOrderNumber || '—' }}</span>
          <span>
            {{ t('crm.salesOrder.contactId') }}: {{ selected.contactNameSnapshot || selected.contactId || '—' }}
          </span>
          <span>
            {{ t('crm.salesOrder.opportunityId') }}:
            {{ selected.opportunityNameSnapshot || selected.opportunityId || '—' }}
          </span>
          <span>{{ t('crm.salesOrder.paymentTerms') }}: {{ selected.paymentTerms || '—' }}</span>
          <span>{{ t('crm.salesOrder.deliveryTerms') }}: {{ selected.deliveryTerms || '—' }}</span>
          <span class="wide-detail">Terms &amp; Conditions: {{ selected.termsAndConditions || '—' }}</span>
          <span>
            {{ t('crm.salesOrder.subtotal') }}: {{ selected.subtotal.toLocaleString() }} {{ selected.currency }}
          </span>
          <span>
            {{ t('crm.salesOrder.discountTotal') }}: {{ selected.discountTotal.toLocaleString() }}
            {{ selected.currency }}
          </span>
          <span>
            {{ t('crm.salesOrder.taxTotal') }}: {{ selected.taxTotal.toLocaleString() }} {{ selected.currency }}
          </span>
          <span>{{ t('crm.salesOrder.notes') }}: {{ selected.notes || '—' }}</span>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{{ t('crm.salesOrder.product') }}</th>
                <th>{{ t('crm.salesOrder.quantity') }}</th>
                <th>{{ t('crm.salesOrder.reserved') }}</th>
                <th>{{ t('crm.salesOrder.shipped') }}</th>
                <th>{{ t('crm.salesOrder.remaining') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="line in lines" :key="line.basicId">
                <td>{{ line.productCode || line.description }}</td>
                <td>{{ line.quantity }}</td>
                <td>{{ line.reservedQuantity }}</td>
                <td>{{ line.shippedQuantity }}</td>
                <td>{{ line.remainingQuantity }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-workflow-page {
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
.editor header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.page-header h1,
.editor h2 {
  margin: 0;
  font-size: 20px;
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
  border-radius: var(--radius-lg);
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
}
th {
  color: hsl(var(--muted-foreground));
  font-size: 11px;
}
.actions {
  white-space: nowrap;
}
.actions button {
  margin-right: 4px;
}
.create-editor {
  width: min(1050px, 100%);
}
.create-editor form {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-top: 16px;
}
.form-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}
.form-grid .form-field,
.line-editor .form-field,
.customer-picker {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
  font-size: 12px;
  color: hsl(var(--muted-foreground));
}
.form-grid .wide {
  grid-column: 1/-1;
}
.customer-picker .x-input {
  flex: 1;
  min-width: 0;
}
.line-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.line-heading h3 {
  margin: 0;
  font-size: 15px;
}
.line-editor {
  display: grid;
  grid-template-columns: 1fr 2fr 1fr 1fr 1fr 1fr 1fr auto;
  gap: 8px;
  align-items: end;
  padding: 12px;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
}
.line-editor .form-field {
  min-width: 0;
}
.remove-line {
  white-space: nowrap;
}
.total-preview {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.total-preview small {
  color: hsl(var(--muted-foreground));
}
.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
@media (max-width: 900px) {
  .line-editor {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
  .description-field {
    grid-column: span 2;
  }
}
@media (max-width: 680px) {
  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .line-editor {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
.empty {
  padding: 44px;
  text-align: center;
  color: hsl(var(--muted-foreground));
}
.pagination {
  font-size: 12px;
  color: hsl(var(--muted-foreground));
}
.pagination div {
  display: flex;
  gap: 6px;
}
.order-detail-grid .wide-detail {
  grid-column: 1/-1;
  white-space: pre-wrap;
}
.order-detail-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin: 14px 0;
  font-size: 12px;
  color: hsl(var(--muted-foreground));
}
@media (max-width: 680px) {
  .order-detail-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
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
  width: min(900px, 100%);
  max-height: 90vh;
  overflow: auto;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius-xl);
  padding: 20px;
  background: hsl(var(--card));
}
@media (max-width: 680px) {
  .crm-workflow-page {
    padding: 12px;
  }
}
</style>
