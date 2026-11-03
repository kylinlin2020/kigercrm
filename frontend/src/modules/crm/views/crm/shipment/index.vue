<script setup lang="ts">
import type { CrmSalesOrderLineDto, CrmSalesOrderListItemDto, CrmShipmentListItemDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmSalesOrderStatus, CrmShipmentStatus, crmSalesOrderApi, crmShipmentApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmShipmentPage' })
const { t } = useI18n()
const { hasPermission } = usePermission()
const rows = ref<CrmShipmentListItemDto[]>([])
const loading = ref(false)
const keyword = ref('')
const status = ref<CrmShipmentStatus | ''>('')
const pageIndex = ref(1)
const totalCount = ref(0)
const pageSize = 20
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))
const canCreate = computed(() => hasPermission('crm_shipments.create'))
const canExecute = computed(() => hasPermission('crm_shipments.execute'))

const createOpen = ref(false)
const saving = ref(false)
const loadingOrders = ref(false)
const loadingLines = ref(false)
const form = ref({ shipmentNumber: '', salesOrderId: '', carrier: '', trackingNumber: '' })
const eligibleOrders = ref<CrmSalesOrderListItemDto[]>([])
const orderLines = ref<CrmSalesOrderLineDto[]>([])
const selectedLineIds = ref<string[]>([])
const quantities = reactive<Record<string, number>>({})
const selectedOrder = computed(() => eligibleOrders.value.find(order => String(order.basicId) === form.value.salesOrderId) || null)
const selectedLines = computed(() => orderLines.value.filter(line => selectedLineIds.value.includes(String(line.basicId))))
function availableQuantity(line: CrmSalesOrderLineDto) {
  return Math.max(0, line.quantity - line.reservedQuantity - line.shippedQuantity)
}
function formatQuantity(value: number) {
  return Number(value.toFixed(4)).toString()
}

async function loadPage() {
  loading.value = true
  try {
    const p = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const r = await crmShipmentApi.page({ ...p, keyword: keyword.value.trim() || null, status: status.value === '' ? null : status.value })
    rows.value = r.items
    totalCount.value = r.page.totalCount
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.shipment.loadError'))
  }
  finally {
    loading.value = false
  }
}

async function openCreate() {
  form.value = { shipmentNumber: '', salesOrderId: '', carrier: '', trackingNumber: '' }
  orderLines.value = []
  selectedLineIds.value = []
  for (const key of Object.keys(quantities)) delete quantities[key]
  createOpen.value = true
  loadingOrders.value = true
  try {
    const statuses = [CrmSalesOrderStatus.Confirmed, CrmSalesOrderStatus.Processing, CrmSalesOrderStatus.PartiallyShipped]
    const results = await Promise.all(statuses.map(async orderStatus => {
      const p = createPageRequest({ page: { pageIndex: 1, pageSize: 100 } })
      return crmSalesOrderApi.page({ ...p, status: orderStatus })
    }))
    eligibleOrders.value = results.flatMap(result => result.items)
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.shipment.ordersError'))
  }
  finally {
    loadingOrders.value = false
  }
}

async function onOrderSelected() {
  orderLines.value = []
  selectedLineIds.value = []
  for (const key of Object.keys(quantities)) delete quantities[key]
  if (!form.value.salesOrderId) return
  loadingLines.value = true
  try {
    const p = createPageRequest({ page: { pageIndex: 1, pageSize: 500 } })
    const result = await crmSalesOrderApi.lines({ ...p, salesOrderId: form.value.salesOrderId })
    orderLines.value = result.items.filter(line => availableQuantity(line) > 0)
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.shipment.linesError'))
  }
  finally {
    loadingLines.value = false
  }
}

function toggleLine(line: CrmSalesOrderLineDto) {
  const id = String(line.basicId)
  if (selectedLineIds.value.includes(id)) {
    selectedLineIds.value = selectedLineIds.value.filter(value => value !== id)
    delete quantities[id]
  }
  else {
    selectedLineIds.value = [...selectedLineIds.value, id]
    quantities[id] = Math.min(1, availableQuantity(line))
  }
}

async function createShipment() {
  if (!form.value.shipmentNumber.trim() || !form.value.salesOrderId || selectedLines.value.length === 0) {
    toast.warning(t('crm.shipment.required'))
    return
  }
  const invalidLine = selectedLines.value.find(line => {
    const quantity = Number(quantities[String(line.basicId)])
    return !Number.isFinite(quantity) || quantity <= 0 || quantity > availableQuantity(line)
      || Math.abs(quantity * 10000 - Math.round(quantity * 10000)) > 1e-8
  })
  if (invalidLine) {
    toast.warning(t('crm.shipment.quantityInvalid'))
    return
  }
  saving.value = true
  try {
    await crmShipmentApi.create({
      shipmentNumber: form.value.shipmentNumber.trim(),
      salesOrderId: form.value.salesOrderId,
      carrier: form.value.carrier.trim() || null,
      trackingNumber: form.value.trackingNumber.trim() || null,
      lines: selectedLines.value.map(line => ({
        salesOrderLineId: String(line.basicId),
        quantity: Number(quantities[String(line.basicId)]),
      })),
    })
    toast.success(t('crm.shipment.created'))
    createOpen.value = false
    await loadPage()
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.shipment.saveError'))
  }
  finally {
    saving.value = false
  }
}

async function changeStatus(row: CrmShipmentListItemDto, next: CrmShipmentStatus) {
  try {
    await crmShipmentApi.changeStatus({ id: row.basicId, status: next })
    toast.success(t('crm.shipment.statusSaved'))
    await loadPage()
  }
  catch (e) {
    toast.danger((e as Error)?.message || t('crm.shipment.statusError'))
  }
}
function label(s: number) {
  return t(`crm.shipment.status${s}`)
}
onMounted(loadPage)
</script>

<template>
  <main class="crm-workflow-page">
    <header class="page-header">
      <div><h1>{{ t('crm.shipment.title') }}</h1><p>{{ t('crm.shipment.description') }}</p></div>
      <button v-if="canCreate" class="primary-button" @click="openCreate">{{ t('crm.shipment.create') }}</button>
    </header>
    <section class="toolbar">
      <input v-model="keyword" :placeholder="t('crm.shipment.search')" @keydown.enter="pageIndex = 1; loadPage()">
      <select v-model="status">
        <option value="">{{ t('crm.shipment.allStatuses') }}</option>
        <option v-for="s in [0, 1, 2, 3]" :key="s" :value="s">{{ label(s) }}</option>
      </select>
      <button @click="pageIndex = 1; loadPage()">{{ t('crm.shipment.query') }}</button>
      <button :disabled="loading" @click="loadPage">{{ t('crm.shipment.refresh') }}</button>
    </section>
    <section class="table-wrap">
      <div v-if="loading && !rows.length" class="empty">{{ t('crm.shipment.loading') }}</div>
      <div v-else-if="!rows.length" class="empty">{{ t('crm.shipment.empty') }}</div>
      <table v-else>
        <thead><tr><th>{{ t('crm.shipment.number') }}</th><th>{{ t('crm.shipment.orderId') }}</th><th>{{ t('crm.shipment.status') }}</th><th>{{ t('crm.shipment.carrier') }}</th><th>{{ t('crm.shipment.tracking') }}</th><th>{{ t('crm.shipment.shippedAt') }}</th><th>{{ t('crm.shipment.deliveredAt') }}</th><th>{{ t('crm.shipment.actions') }}</th></tr></thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td>{{ row.shipmentNumber }}</td><td>{{ row.salesOrderId }}</td><td>{{ label(row.status) }}</td>
            <td>{{ row.carrier || '—' }}</td><td>{{ row.trackingNumber || '—' }}</td>
            <td>{{ row.shipmentDate ? new Date(row.shipmentDate).toLocaleString() : '—' }}</td>
            <td>{{ row.deliveredDate ? new Date(row.deliveredDate).toLocaleString() : '—' }}</td>
            <td class="actions">
              <button v-if="canExecute && row.status === CrmShipmentStatus.Draft" @click="changeStatus(row, CrmShipmentStatus.Dispatched)">{{ t('crm.shipment.dispatch') }}</button>
              <button v-if="canExecute && row.status === CrmShipmentStatus.Draft" @click="changeStatus(row, CrmShipmentStatus.Cancelled)">{{ t('crm.shipment.cancelDraft') }}</button>
              <button v-if="canExecute && row.status === CrmShipmentStatus.Dispatched" @click="changeStatus(row, CrmShipmentStatus.Delivered)">{{ t('crm.shipment.deliver') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <footer class="pagination">
      <span>{{ t('crm.shipment.totalCount', { count: totalCount }) }} · {{ t('crm.shipment.page', { current: pageIndex, total: totalPages }) }}</span>
      <div><button :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.shipment.previous') }}</button><button :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.shipment.next') }}</button></div>
    </footer>

    <div v-if="createOpen" class="modal-backdrop" @click.self="createOpen = false">
      <section class="editor">
        <header><h2>{{ t('crm.shipment.newTitle') }}</h2><button type="button" @click="createOpen = false">×</button></header>
        <form @submit.prevent="createShipment">
          <label>{{ t('crm.shipment.number') }} *<input v-model="form.shipmentNumber" required maxlength="40"></label>
          <label>{{ t('crm.shipment.orderSelect') }} *
            <select v-model="form.salesOrderId" required :disabled="loadingOrders" @change="onOrderSelected">
              <option value="">{{ loadingOrders ? t('crm.shipment.loadingOrders') : t('crm.shipment.selectOrder') }}</option>
              <option v-for="order in eligibleOrders" :key="order.basicId" :value="String(order.basicId)">{{ order.orderNumber }} · {{ order.customerNameSnapshot }} · {{ order.grandTotal }} {{ order.currency }}</option>
            </select>
          </label>
          <p v-if="selectedOrder" class="order-context">{{ selectedOrder.customerNameSnapshot }} · {{ selectedOrder.orderNumber }} · {{ label(selectedOrder.status) }}</p>
          <div class="line-picker">
            <div class="line-heading"><strong>{{ t('crm.shipment.selectLines') }}</strong><span v-if="selectedLines.length">{{ t('crm.shipment.selectedLines', { count: selectedLines.length }) }}</span></div>
            <div v-if="loadingLines" class="empty">{{ t('crm.shipment.loadingLines') }}</div>
            <div v-else-if="form.salesOrderId && !orderLines.length" class="empty">{{ t('crm.shipment.noAvailableLines') }}</div>
            <div v-else-if="!form.salesOrderId" class="empty">{{ t('crm.shipment.chooseOrderFirst') }}</div>
            <div v-else class="line-list">
              <div v-for="line in orderLines" :key="line.basicId" class="line-row">
                <label class="line-check"><input type="checkbox" :checked="selectedLineIds.includes(String(line.basicId))" @change="toggleLine(line)"><span>{{ line.productCode || line.description }}</span></label>
                <span>{{ t('crm.shipment.available') }}: {{ formatQuantity(availableQuantity(line)) }}</span>
                <input v-if="selectedLineIds.includes(String(line.basicId))" v-model.number="quantities[String(line.basicId)]" type="number" min="0.0001" :max="availableQuantity(line)" step="0.0001" :aria-label="t('crm.shipment.quantity')">
              </div>
            </div>
          </div>
          <label>{{ t('crm.shipment.carrier') }}<input v-model="form.carrier" maxlength="100"></label>
          <label>{{ t('crm.shipment.tracking') }}<input v-model="form.trackingNumber" maxlength="120"></label>
          <footer><button type="button" @click="createOpen = false">{{ t('crm.shipment.cancel') }}</button><button class="primary-button" type="submit" :disabled="saving || loadingOrders || loadingLines">{{ saving ? t('crm.shipment.saving') : t('crm.shipment.save') }}</button></footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-workflow-page{display:flex;flex-direction:column;gap:16px;min-height:100%;padding:20px;color:hsl(var(--foreground))}
.page-header,.toolbar,.pagination,.editor header,.editor footer,.line-heading{display:flex;align-items:center;justify-content:space-between;gap:12px}
.page-header h1,.editor h2{margin:0;font-size:20px}.page-header p{margin:5px 0 0;color:hsl(var(--muted-foreground));font-size:12px}
button,input,select{font:inherit}button{border:1px solid hsl(var(--border));border-radius:7px;padding:7px 11px;background:hsl(var(--card));cursor:pointer}button:disabled{opacity:.55;cursor:not-allowed}
.primary-button{background:hsl(var(--primary));color:hsl(var(--primary-foreground))}.toolbar{justify-content:flex-start;flex-wrap:wrap}
.toolbar input,.toolbar select,.editor input,.editor select{border:1px solid hsl(var(--border));border-radius:7px;padding:8px 10px;background:hsl(var(--card));color:inherit}
.table-wrap{overflow:auto;border:1px solid hsl(var(--border));border-radius:10px;background:hsl(var(--card))}table{width:100%;border-collapse:collapse;min-width:950px;text-align:left;font-size:13px}
th,td{padding:11px 12px;border-bottom:1px solid hsl(var(--border))}th{color:hsl(var(--muted-foreground));font-size:11px}.actions{white-space:nowrap}.actions button{margin-right:4px;font-size:11px}
.empty{padding:24px;text-align:center;color:hsl(var(--muted-foreground));font-size:12px}.pagination{font-size:12px;color:hsl(var(--muted-foreground))}.pagination div{display:flex;gap:6px}
.modal-backdrop{position:fixed;z-index:1000;inset:0;display:grid;place-items:center;padding:20px;background:rgb(0 0 0 / 42%)}
.editor{width:min(820px,100%);max-height:90vh;overflow:auto;border:1px solid hsl(var(--border));border-radius:12px;padding:20px;background:hsl(var(--card))}
.editor form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px;margin-top:18px}.editor form>label{display:flex;flex-direction:column;gap:6px;font-size:12px;color:hsl(var(--muted-foreground))}
.editor form>label input,.editor form>label select{box-sizing:border-box;width:100%;color:hsl(var(--foreground))}.order-context,.line-picker{grid-column:1/-1}.order-context{margin:0;color:hsl(var(--muted-foreground));font-size:12px}
.line-picker{border:1px solid hsl(var(--border));border-radius:8px;padding:12px}.line-heading{font-size:12px;margin-bottom:8px}.line-heading span{color:hsl(var(--muted-foreground))}
.line-row{display:grid;grid-template-columns:minmax(180px,1fr) minmax(110px,auto) minmax(110px,140px);align-items:center;gap:12px;padding:9px 0;border-top:1px solid hsl(var(--border));font-size:12px}
.line-check{display:flex;align-items:center;gap:8px;min-width:0}.line-check input{width:auto}.line-check span{overflow-wrap:anywhere}.line-row>span{color:hsl(var(--muted-foreground))}.line-row>input{width:100%;box-sizing:border-box}
.editor form footer{grid-column:1/-1;justify-content:flex-end}
@media(max-width:680px){.crm-workflow-page{padding:12px}.editor form{grid-template-columns:1fr}.editor form footer{grid-column:auto}.line-row{grid-template-columns:minmax(120px,1fr) minmax(90px,auto)}.line-row>input{grid-column:2}.line-check{grid-column:1}.line-picker,.order-context{grid-column:auto}}
</style>
