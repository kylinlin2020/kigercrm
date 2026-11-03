<script setup lang="ts">
import type { CrmQuoteCreateDto, CrmQuoteDetailDto, CrmQuoteLineCreateDto, CrmQuoteLineDto, CrmQuoteListItemDto } from '@/api'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmQuoteStatus, crmQuoteApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmQuotePage' })
const { t } = useI18n()
const { hasPermission } = usePermission()
const rows = ref<CrmQuoteListItemDto[]>([])
const loading = ref(false)
const keyword = ref('')
const status = ref<CrmQuoteStatus | ''>('')
const pageIndex = ref(1)
const totalCount = ref(0)
const pageSize = 20
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))
const canCreate = computed(() => hasPermission('crm_quotes.create'))
const canExecute = computed(() => hasPermission('crm_quotes.execute'))
const detailOpen = ref(false)
const detailLoading = ref(false)
const detailQuote = ref<CrmQuoteDetailDto | null>(null)
const detailLines = ref<CrmQuoteLineDto[]>([])
const reviseOpen = ref(false)
const revising = ref(false)
const reviseSourceId = ref<string | null>(null)
const revisionBase = ref<{ quoteNumber: string, customerId: string, currency: string } | null>(null)
const reviseForm = ref({ issueDate: new Date().toISOString().slice(0, 10), validUntil: '', termsAndConditions: '', notes: '' })
const reviseLines = ref<CrmQuoteLineCreateDto[]>([])
const createOpen = ref(false)
const saving = ref(false)
const form = ref({ quoteNumber: '', customerId: '', issueDate: new Date().toISOString().slice(0, 10), validUntil: '', currency: 'USD', description: '', quantity: '1', unitPrice: '0' })

async function loadPage() {
  loading.value = true
  try {
    const page = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmQuoteApi.page({ ...page, keyword: keyword.value.trim() || null, status: status.value === '' ? null : status.value })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.loadError')) }
  finally { loading.value = false }
}
function search() { pageIndex.value = 1; void loadPage() }
async function createQuote() {
  const quantity = Number(form.value.quantity)
  const unitPrice = Number(form.value.unitPrice)
  if (!form.value.quoteNumber.trim() || !form.value.customerId.trim() || !form.value.description.trim() || !Number.isFinite(quantity) || quantity <= 0 || !Number.isFinite(unitPrice) || unitPrice < 0) {
    toast.warning(t('crm.quote.required')); return
  }
  saving.value = true
  try {
    await crmQuoteApi.create({
      quoteNumber: form.value.quoteNumber.trim(), customerId: form.value.customerId.trim(),
      issueDate: `${form.value.issueDate}T00:00:00`, validUntil: form.value.validUntil ? `${form.value.validUntil}T23:59:59` : null,
      currency: form.value.currency.trim().toUpperCase(),
      lines: [{ description: form.value.description.trim(), quantity, unitPrice, discountRate: 0, taxRate: 0, sortOrder: 0 }],
    })
    toast.success(t('crm.quote.created')); createOpen.value = false; await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.saveError')) }
  finally { saving.value = false }
}
async function changeStatus(row: CrmQuoteListItemDto, next: CrmQuoteStatus) {
  try { await crmQuoteApi.changeStatus({ id: row.basicId, status: next }); toast.success(t('crm.quote.statusSaved')); await loadPage() }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.statusError')) }
}
async function openDetails(row: CrmQuoteListItemDto) {
  detailOpen.value = true; detailLoading.value = true; detailQuote.value = null; detailLines.value = []
  try {
    const page = createPageRequest({ page: { pageIndex: 1, pageSize: 500 } })
    const [detail, lines] = await Promise.all([crmQuoteApi.detail(row.basicId), crmQuoteApi.lines({ ...page, quoteId: row.basicId })])
    if (!detail) throw new Error(t('crm.quote.detailError'))
    detailQuote.value = detail; detailLines.value = lines.items
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.detailError')) }
  finally { detailLoading.value = false }
}
function closeDetails() { detailOpen.value = false; detailQuote.value = null }
function openRevision() {
  if (!detailQuote.value) return
  reviseSourceId.value = String(detailQuote.value.basicId)
  revisionBase.value = { quoteNumber: detailQuote.value.quoteNumber, customerId: String(detailQuote.value.customerId), currency: detailQuote.value.currency }
  reviseForm.value = { issueDate: new Date().toISOString().slice(0, 10), validUntil: detailQuote.value.validUntil ? detailQuote.value.validUntil.slice(0, 10) : '', termsAndConditions: detailQuote.value.termsAndConditions || '', notes: detailQuote.value.notes || '' }
  reviseLines.value = detailLines.value.map((line, index) => ({ productCode: line.productCode, description: line.description, quantity: line.quantity, unit: line.unit, unitPrice: line.unitPrice, discountRate: line.discountRate, taxRate: line.taxRate, sortOrder: line.sortOrder ?? index }))
  closeDetails(); reviseOpen.value = true
}
async function saveRevision() {
  if (!reviseSourceId.value || !revisionBase.value || reviseLines.value.length === 0 || !reviseForm.value.issueDate || (reviseForm.value.validUntil && reviseForm.value.validUntil < reviseForm.value.issueDate) || reviseLines.value.some(line => !line.description.trim() || !Number.isFinite(line.quantity) || line.quantity <= 0 || Math.abs(line.quantity * 10000 - Math.round(line.quantity * 10000)) > 1e-8 || !Number.isFinite(line.unitPrice) || line.unitPrice < 0 || !Number.isFinite(line.discountRate) || line.discountRate < 0 || line.discountRate > 100 || !Number.isFinite(line.taxRate) || line.taxRate < 0 || line.taxRate > 100)) { toast.warning(t('crm.quote.revisionRequired')); return }
  revising.value = true
  try {
    const input: CrmQuoteCreateDto = { quoteNumber: revisionBase.value.quoteNumber, customerId: revisionBase.value.customerId, issueDate: `${reviseForm.value.issueDate}T00:00:00`, validUntil: reviseForm.value.validUntil ? `${reviseForm.value.validUntil}T23:59:59` : null, currency: revisionBase.value.currency, termsAndConditions: reviseForm.value.termsAndConditions || null, notes: reviseForm.value.notes || null, lines: reviseLines.value }
    await crmQuoteApi.revise(reviseSourceId.value, input)
    toast.success(t('crm.quote.revised')); reviseOpen.value = false; reviseSourceId.value = null; revisionBase.value = null; await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.revisionError')) }
  finally { revising.value = false }
}
async function convertToOrder(row: CrmQuoteListItemDto) {
  try { const order = await crmQuoteApi.convertToSalesOrder(row.basicId); toast.success(`${t('crm.quote.converted')}: ${order.orderNumber}`); await loadPage() }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.convertError')) }
}
function statusLabel(value: number) { return t(`crm.quote.status${value}`) }
onMounted(loadPage)
</script>

<template>
  <main class="crm-workflow-page">
    <header class="page-header"><div><h1>{{ t('crm.quote.title') }}</h1><p>{{ t('crm.quote.description') }}</p></div><button v-if="canCreate" class="primary-button" @click="createOpen = true">{{ t('crm.quote.create') }}</button></header>
    <section class="toolbar"><input v-model="keyword" :placeholder="t('crm.quote.search')" @keydown.enter="search"><select v-model="status"><option value="">{{ t('crm.quote.allStatuses') }}</option><option v-for="s in [0,1,2,3,4,5]" :key="s" :value="s">{{ statusLabel(s) }}</option></select><button @click="search">{{ t('crm.quote.query') }}</button><button :disabled="loading" @click="loadPage">{{ t('crm.quote.refresh') }}</button></section>
    <section class="table-wrap"><div v-if="loading && !rows.length" class="empty">{{ t('crm.quote.loading') }}</div><div v-else-if="!rows.length" class="empty">{{ t('crm.quote.empty') }}</div><table v-else><thead><tr><th>{{ t('crm.quote.number') }}</th><th>{{ t('crm.quote.customer') }}</th><th>{{ t('crm.quote.version') }}</th><th>{{ t('crm.quote.status') }}</th><th>{{ t('crm.quote.date') }}</th><th>{{ t('crm.quote.totalCount') }}</th><th>{{ t('crm.quote.actions') }}</th></tr></thead><tbody><tr v-for="row in rows" :key="row.basicId"><td>{{ row.quoteNumber }}</td><td>{{ row.customerNameSnapshot }} (#{{ row.customerId }})</td><td>{{ row.version }}</td><td>{{ statusLabel(row.status) }}</td><td>{{ new Date(row.issueDate).toLocaleDateString() }}</td><td>{{ row.grandTotal.toLocaleString() }} {{ row.currency }}</td><td class="actions"><button @click="openDetails(row)">{{ t('crm.quote.viewDetails') }}</button><button v-if="canExecute && row.status === CrmQuoteStatus.Draft" @click="changeStatus(row, CrmQuoteStatus.Sent)">{{ t('crm.quote.markSent') }}</button><button v-if="canExecute && row.status === CrmQuoteStatus.Sent" @click="changeStatus(row, CrmQuoteStatus.Accepted)">{{ t('crm.quote.accept') }}</button><button v-if="canExecute && row.status === CrmQuoteStatus.Sent" @click="changeStatus(row, CrmQuoteStatus.Rejected)">{{ t('crm.quote.reject') }}</button><button v-if="canExecute && row.status === CrmQuoteStatus.Accepted" @click="convertToOrder(row)">{{ t('crm.quote.convert') }}</button></td></tr></tbody></table></section>
    <footer class="pagination"><span>{{ t('crm.quote.totalCount', { count: totalCount }) }} · {{ t('crm.quote.page', { current: pageIndex, total: totalPages }) }}</span><div><button :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.quote.previous') }}</button><button :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.quote.next') }}</button></div></footer>
    <div v-if="detailOpen" class="modal-backdrop" @click.self="closeDetails">
      <section class="editor quote-detail-editor">
        <header><h2>{{ detailQuote?.quoteNumber || t('crm.quote.detailTitle') }}</h2><button type="button" @click="closeDetails">×</button></header>
        <div v-if="detailLoading" class="empty">{{ t('crm.quote.loadingDetails') }}</div>
        <template v-else-if="detailQuote">
          <div class="detail-grid">
            <p>{{ t('crm.quote.customer') }}: {{ detailQuote.customerNameSnapshot }} (#{{ detailQuote.customerId }})</p>
            <p>{{ t('crm.quote.version') }}: {{ detailQuote.version }}</p>
            <p>{{ t('crm.quote.status') }}: {{ statusLabel(detailQuote.status) }}</p>
            <p>{{ t('crm.quote.date') }}: {{ new Date(detailQuote.issueDate).toLocaleDateString() }}</p>
            <p>{{ t('crm.quote.total') }}: {{ detailQuote.grandTotal.toLocaleString() }} {{ detailQuote.currency }}</p>
            <p v-if="detailQuote.convertedOrderId">{{ t('crm.quote.convertedOrder') }}: #{{ detailQuote.convertedOrderId }}</p>
          </div>
          <div class="table-wrap"><table><thead><tr><th>{{ t('crm.quote.descriptionLine') }}</th><th>{{ t('crm.quote.quantity') }}</th><th>{{ t('crm.quote.unitPrice') }}</th><th>{{ t('crm.quote.total') }}</th></tr></thead><tbody>
            <tr v-for="line in detailLines" :key="line.basicId"><td>{{ line.description }}</td><td>{{ line.quantity }} {{ line.unit || '' }}</td><td>{{ line.unitPrice.toLocaleString() }}</td><td>{{ line.lineTotal.toLocaleString() }}</td></tr>
            <tr v-if="!detailLines.length"><td colspan="4" class="empty">{{ t('crm.quote.noLines') }}</td></tr>
          </tbody></table></div>
          <p v-if="detailQuote.termsAndConditions" class="detail-notes">{{ t('crm.quote.terms') }}: {{ detailQuote.termsAndConditions }}</p>
          <p v-if="detailQuote.notes" class="detail-notes">{{ t('crm.quote.notes') }}: {{ detailQuote.notes }}</p>
          <footer class="detail-actions"><button v-if="canCreate && [CrmQuoteStatus.Sent, CrmQuoteStatus.Rejected, CrmQuoteStatus.Expired].includes(detailQuote.status)" @click="openRevision">{{ t('crm.quote.createRevision') }}</button></footer>
        </template>
      </section>
    </div>
    <div v-if="reviseOpen" class="modal-backdrop" @click.self="reviseOpen = false">
      <section class="editor revision-editor">
        <header><h2>{{ t('crm.quote.revisionTitle') }}</h2><button type="button" @click="reviseOpen = false">×</button></header>
        <form @submit.prevent="saveRevision">
          <label>{{ t('crm.quote.issueDate') }} *<input v-model="reviseForm.issueDate" type="date" required></label>
          <label>{{ t('crm.quote.validUntil') }}<input v-model="reviseForm.validUntil" type="date"></label>
          <label class="full-width">{{ t('crm.quote.terms') }}<textarea v-model="reviseForm.termsAndConditions" rows="2"></textarea></label>
          <label class="full-width">{{ t('crm.quote.notes') }}<textarea v-model="reviseForm.notes" rows="2"></textarea></label>
          <div class="revision-lines full-width"><strong>{{ t('crm.quote.revisionLines') }} ({{ reviseLines.length }})</strong>
            <div v-for="(line, index) in reviseLines" :key="index" class="revision-line">
              <label>{{ t('crm.quote.descriptionLine') }}<input v-model="line.description" required maxlength="500"></label>
              <label>{{ t('crm.quote.quantity') }}<input v-model.number="line.quantity" type="number" min="0.0001" step="0.0001" required></label>
              <label>{{ t('crm.quote.unitPrice') }}<input v-model.number="line.unitPrice" type="number" min="0" step="0.0001" required></label>
              <label>{{ t('crm.quote.discountRate') }}<input v-model.number="line.discountRate" type="number" min="0" max="100" step="0.0001"></label>
              <label>{{ t('crm.quote.taxRate') }}<input v-model.number="line.taxRate" type="number" min="0" max="100" step="0.0001"></label>
            </div>
          </div>
          <footer><button type="button" @click="reviseOpen = false">{{ t('crm.quote.cancel') }}</button><button class="primary-button" type="submit" :disabled="revising">{{ revising ? t('crm.quote.saving') : t('crm.quote.saveRevision') }}</button></footer>
        </form>
      </section>
    </div>
    <div v-if="createOpen" class="modal-backdrop" @click.self="createOpen = false"><section class="editor"><header><h2>{{ t('crm.quote.newTitle') }}</h2><button @click="createOpen = false">×</button></header><form @submit.prevent="createQuote"><label>{{ t('crm.quote.number') }} *<input v-model="form.quoteNumber" required maxlength="40"></label><label>{{ t('crm.quote.customerId') }} *<input v-model="form.customerId" required inputmode="numeric"></label><label>{{ t('crm.quote.issueDate') }} *<input v-model="form.issueDate" type="date" required></label><label>{{ t('crm.quote.validUntil') }}<input v-model="form.validUntil" type="date"></label><label>{{ t('crm.quote.currency') }}<input v-model="form.currency" maxlength="3" required></label><label>{{ t('crm.quote.descriptionLine') }} *<input v-model="form.description" required maxlength="500"></label><label>{{ t('crm.quote.quantity') }} *<input v-model="form.quantity" type="number" min="0.0001" step="0.0001" required></label><label>{{ t('crm.quote.unitPrice') }} *<input v-model="form.unitPrice" type="number" min="0" step="0.01" required></label><footer><button type="button" @click="createOpen = false">{{ t('crm.quote.cancel') }}</button><button class="primary-button" type="submit" :disabled="saving">{{ saving ? t('crm.quote.saving') : t('crm.quote.save') }}</button></footer></form></section></div>
  </main>
</template>
<style scoped>
.crm-workflow-page{display:flex;flex-direction:column;gap:16px;min-height:100%;padding:20px;color:hsl(var(--foreground))}.page-header,.toolbar,.pagination,.editor header,.editor footer{display:flex;align-items:center;justify-content:space-between;gap:12px}.page-header h1,.editor h2{margin:0;font-size:20px;font-weight:650}.page-header p{margin:5px 0 0;color:hsl(var(--muted-foreground));font-size:12px}button,input,select{font:inherit}button{border:1px solid hsl(var(--border));border-radius:7px;padding:7px 11px;background:hsl(var(--card));cursor:pointer}button:disabled{opacity:.55}.primary-button{background:hsl(var(--primary));color:hsl(var(--primary-foreground))}.toolbar{justify-content:flex-start;flex-wrap:wrap}.toolbar input,.toolbar select,.editor input{border:1px solid hsl(var(--border));border-radius:7px;padding:8px 10px;background:hsl(var(--card));color:inherit}.table-wrap{overflow:auto;border:1px solid hsl(var(--border));border-radius:10px;background:hsl(var(--card))}table{width:100%;border-collapse:collapse;min-width:800px;text-align:left;font-size:13px}th,td{padding:11px 12px;border-bottom:1px solid hsl(var(--border))}th{color:hsl(var(--muted-foreground));font-size:11px}.actions{white-space:nowrap}.actions button{margin-right:4px;font-size:11px}.empty{padding:44px;text-align:center;color:hsl(var(--muted-foreground))}.pagination{font-size:12px;color:hsl(var(--muted-foreground))}.pagination div{display:flex;gap:6px}.modal-backdrop{position:fixed;z-index:1000;inset:0;display:grid;place-items:center;padding:20px;background:rgb(0 0 0 / 42%)}.editor{width:min(720px,100%);max-height:90vh;overflow:auto;border:1px solid hsl(var(--border));border-radius:12px;padding:20px;background:hsl(var(--card))}.quote-detail-editor{width:min(900px,100%)}.detail-actions{display:flex;justify-content:flex-end;margin-top:12px}.revision-editor{width:min(900px,100%)}.revision-editor textarea{border:1px solid hsl(var(--border));border-radius:7px;padding:8px;background:hsl(var(--card));color:inherit}.full-width{grid-column:1/-1}.revision-lines{border:1px solid hsl(var(--border));border-radius:8px;padding:12px}.revision-line{display:grid;grid-template-columns:2fr repeat(4,minmax(90px,1fr));gap:8px;padding:10px 0;border-top:1px solid hsl(var(--border))}.revision-line label{display:flex;flex-direction:column;gap:4px;font-size:11px;color:hsl(var(--muted-foreground))}.revision-line input{min-width:0;width:100%;box-sizing:border-box}.detail-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:4px 16px;font-size:12px}.detail-grid p,.detail-notes{margin:5px 0;color:hsl(var(--muted-foreground))}.quote-detail-editor .table-wrap{margin-top:12px}.editor form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px;margin-top:18px}.editor label{display:flex;flex-direction:column;gap:6px;font-size:12px;color:hsl(var(--muted-foreground))}.editor label input{box-sizing:border-box;width:100%;color:hsl(var(--foreground))}.editor form footer{grid-column:1/-1;justify-content:flex-end}@media(max-width:680px){.crm-workflow-page{padding:12px}.editor form{grid-template-columns:1fr}.editor form footer{grid-column:auto}.revision-line{grid-template-columns:1fr 1fr}.full-width{grid-column:auto}.detail-grid{grid-template-columns:1fr}}
</style>
