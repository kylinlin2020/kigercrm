<script setup lang="ts">
import type { CrmQuoteCreateDto, CrmQuoteDetailDto, CrmQuoteLineCreateDto, CrmQuoteLineDto, CrmQuoteListItemDto } from '@/api'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmQuoteApi, CrmQuoteStatus } from '@/api'
import { XDatePicker, XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmQuotePage' })
const { t } = useI18n()
const { hasPermission } = usePermission()

type NumericLineField = 'quantity' | 'unitPrice' | 'discountRate' | 'taxRate'
type QuoteLineForm = Omit<CrmQuoteLineCreateDto, NumericLineField> & Record<NumericLineField, string>

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
const reviseForm = ref({ issueDate: fromDateString(new Date().toISOString().slice(0, 10)), validUntil: null as number | null, termsAndConditions: '', notes: '' })
const reviseLines = ref<QuoteLineForm[]>([])
const createOpen = ref(false)
const saving = ref(false)
const createIdempotencyKey = ref('')
function newIdempotencyKey() {
  return globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`
}
const form = ref({ quoteNumber: '', customerId: '', issueDate: fromDateString(new Date().toISOString().slice(0, 10)), validUntil: null as number | null, currency: 'USD', description: '', quantity: '1', unitPrice: '0' })

function statusLabel(value: number) {
  return t(`crm.quote.status${value}`)
}

// XSelect 用字符串选项；这里把值还原成 number | ''，保持原数字枚举语义。
const statusOptions = computed(() => [
  { label: t('crm.quote.allStatuses'), value: '' },
  ...[0, 1, 2, 3, 4, 5].map(s => ({ label: statusLabel(s), value: String(s) })),
])
const statusValue = computed({
  get: () => String(status.value),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    status.value = next == null || next === '' ? '' : Number(next) as CrmQuoteStatus
  },
})

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
function search() {
  pageIndex.value = 1
  void loadPage()
}
async function createQuote() {
  const quantity = Number(form.value.quantity)
  const unitPrice = Number(form.value.unitPrice)
  if (!form.value.customerId.trim() || !form.value.description.trim() || !Number.isFinite(quantity) || quantity <= 0 || !Number.isFinite(unitPrice) || unitPrice < 0) {
    toast.warning(t('crm.quote.required'))
    return
  }
  saving.value = true
  try {
    await crmQuoteApi.create({
      quoteNumber: form.value.quoteNumber.trim(),
      numberingIdempotencyKey: createIdempotencyKey.value,
      customerId: form.value.customerId.trim(),
      issueDate: `${toDateString(form.value.issueDate)}T00:00:00`,
      validUntil: form.value.validUntil ? `${toDateString(form.value.validUntil)}T23:59:59` : null,
      currency: form.value.currency.trim().toUpperCase(),
      lines: [{ description: form.value.description.trim(), quantity, unitPrice, discountRate: 0, taxRate: 0, sortOrder: 0 }],
    })
    toast.success(t('crm.quote.created'))
    createOpen.value = false
    createIdempotencyKey.value = ''
    await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.saveError')) }
  finally { saving.value = false }
}
async function changeStatus(row: CrmQuoteListItemDto, next: CrmQuoteStatus) {
  try {
    await crmQuoteApi.changeStatus({ id: row.basicId, status: next })
    toast.success(t('crm.quote.statusSaved'))
    await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.statusError')) }
}
async function openDetails(row: CrmQuoteListItemDto) {
  detailOpen.value = true
  detailLoading.value = true
  detailQuote.value = null
  detailLines.value = []
  try {
    const page = createPageRequest({ page: { pageIndex: 1, pageSize: 500 } })
    const [detail, lines] = await Promise.all([crmQuoteApi.detail(row.basicId), crmQuoteApi.lines({ ...page, quoteId: row.basicId })])
    if (!detail)
      throw new Error(t('crm.quote.detailError'))
    detailQuote.value = detail
    detailLines.value = lines.items
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.detailError')) }
  finally { detailLoading.value = false }
}
function closeDetails() {
  detailOpen.value = false
  detailQuote.value = null
}
async function openPreviousVersion() {
  const id = detailQuote.value?.previousVersionId
  if (id)
    await openDetails({ basicId: id } as CrmQuoteListItemDto)
}
function openRevision() {
  if (!detailQuote.value)
    return
  reviseSourceId.value = String(detailQuote.value.basicId)
  revisionBase.value = { quoteNumber: detailQuote.value.quoteNumber, customerId: String(detailQuote.value.customerId), currency: detailQuote.value.currency }
  reviseForm.value = { issueDate: fromDateString(new Date().toISOString().slice(0, 10)), validUntil: fromDateString(detailQuote.value.validUntil ? detailQuote.value.validUntil.slice(0, 10) : ''), termsAndConditions: detailQuote.value.termsAndConditions || '', notes: detailQuote.value.notes || '' }
  reviseLines.value = detailLines.value.map((line, index) => ({ productCode: line.productCode, description: line.description, quantity: String(line.quantity), unit: line.unit, unitPrice: String(line.unitPrice), discountRate: String(line.discountRate), taxRate: String(line.taxRate), sortOrder: line.sortOrder ?? index }))
  closeDetails()
  reviseOpen.value = true
}
async function saveRevision() {
  const invalidRange = reviseForm.value.validUntil != null
    && reviseForm.value.issueDate != null
    && reviseForm.value.validUntil < reviseForm.value.issueDate
  const invalidLines = reviseLines.value.some(line =>
    !line.description.trim()
    || !Number.isFinite(Number(line.quantity))
    || Number(line.quantity) <= 0
    || Math.abs(Number(line.quantity) * 10000 - Math.round(Number(line.quantity) * 10000)) > 1e-8
    || !Number.isFinite(Number(line.unitPrice))
    || Number(line.unitPrice) < 0
    || !Number.isFinite(Number(line.discountRate))
    || Number(line.discountRate) < 0
    || Number(line.discountRate) > 100
    || !Number.isFinite(Number(line.taxRate))
    || Number(line.taxRate) < 0
    || Number(line.taxRate) > 100)
  if (!reviseSourceId.value || !revisionBase.value || reviseLines.value.length === 0 || !reviseForm.value.issueDate || invalidRange || invalidLines) {
    toast.warning(t('crm.quote.revisionRequired'))
    return
  }
  revising.value = true
  try {
    const input: CrmQuoteCreateDto = { quoteNumber: revisionBase.value.quoteNumber, customerId: revisionBase.value.customerId, issueDate: `${toDateString(reviseForm.value.issueDate)}T00:00:00`, validUntil: reviseForm.value.validUntil ? `${toDateString(reviseForm.value.validUntil)}T23:59:59` : null, currency: revisionBase.value.currency, termsAndConditions: reviseForm.value.termsAndConditions || null, notes: reviseForm.value.notes || null, lines: reviseLines.value.map(line => ({ ...line, quantity: Number(line.quantity), unitPrice: Number(line.unitPrice), discountRate: Number(line.discountRate), taxRate: Number(line.taxRate) })) }
    await crmQuoteApi.revise(reviseSourceId.value, input)
    toast.success(t('crm.quote.revised'))
    reviseOpen.value = false
    reviseSourceId.value = null
    revisionBase.value = null
    await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.revisionError')) }
  finally { revising.value = false }
}
async function convertToOrder(row: CrmQuoteListItemDto) {
  try {
    const order = await crmQuoteApi.convertToSalesOrder(row.basicId)
    toast.success(`${t('crm.quote.converted')}: ${order.orderNumber}`)
    await loadPage()
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.quote.convertError')) }
}
onMounted(loadPage)
</script>

<template>
  <main class="crm-workflow-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.quote.title') }}</h1>
        <p>{{ t('crm.quote.description') }}</p>
      </div>
      <XhButton v-if="canCreate" variant="outline" @click="createIdempotencyKey = newIdempotencyKey(); createOpen = true">
        <XhButtonLabel>{{ t('crm.quote.create') }}</XhButtonLabel>
      </XhButton>
    </header>
    <section class="toolbar">
      <XhFieldRoot class="form-field">
        <XhFieldLabel>{{ t('crm.quote.search') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="keyword" :placeholder="t('crm.quote.search')" @enter="search" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="form-field">
        <XhFieldLabel>{{ t('crm.quote.status') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="statusValue" :options="statusOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.quote.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.quote.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>
    <section class="table-wrap">
      <div v-if="loading && !rows.length" class="empty">
        {{ t('crm.quote.loading') }}
      </div>
      <div v-else-if="!rows.length" class="empty">
        {{ t('crm.quote.empty') }}
      </div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.quote.number') }}</th>
            <th>{{ t('crm.quote.customer') }}</th>
            <th>{{ t('crm.quote.version') }}</th>
            <th>{{ t('crm.quote.status') }}</th>
            <th>{{ t('crm.quote.date') }}</th>
            <th>{{ t('crm.quote.total') }}</th>
            <th>{{ t('crm.quote.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td>{{ row.quoteNumber }}</td>
            <td>{{ row.customerNameSnapshot }} (#{{ row.customerId }})</td>
            <td>{{ row.version }}</td>
            <td>{{ statusLabel(row.status) }}</td>
            <td>{{ new Date(row.issueDate).toLocaleDateString() }}</td>
            <td>{{ row.grandTotal.toLocaleString() }} {{ row.currency }}</td>
            <td class="actions">
              <XhButton variant="subtle" @click="openDetails(row)">
                <XhButtonLabel>{{ t('crm.quote.viewDetails') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canExecute && row.status === CrmQuoteStatus.Draft" variant="outline" @click="changeStatus(row, CrmQuoteStatus.Sent)">
                <XhButtonLabel>{{ t('crm.quote.markSent') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canExecute && row.status === CrmQuoteStatus.Sent" variant="outline" @click="changeStatus(row, CrmQuoteStatus.Accepted)">
                <XhButtonLabel>{{ t('crm.quote.accept') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canExecute && row.status === CrmQuoteStatus.Sent" variant="outline" tone="danger" @click="changeStatus(row, CrmQuoteStatus.Rejected)">
                <XhButtonLabel>{{ t('crm.quote.reject') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canExecute && row.status === CrmQuoteStatus.Accepted" variant="outline" @click="convertToOrder(row)">
                <XhButtonLabel>{{ t('crm.quote.convert') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
    <footer class="pagination">
      <span>{{ t('crm.quote.totalCount', { count: totalCount }) }} · {{ t('crm.quote.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.quote.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.quote.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>
    <div v-if="detailOpen" class="modal-backdrop" @click.self="closeDetails">
      <section class="editor quote-detail-editor">
        <header>
          <h2>{{ detailQuote?.quoteNumber || t('crm.quote.detailTitle') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.quote.cancel')" @click="closeDetails">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <div v-if="detailLoading" class="empty">
          {{ t('crm.quote.loadingDetails') }}
        </div>
        <template v-else-if="detailQuote">
          <div class="detail-grid">
            <p>{{ t('crm.quote.customer') }}: {{ detailQuote.customerNameSnapshot }} (#{{ detailQuote.customerId }})</p>
            <p>
              {{ t('crm.quote.version') }}: {{ detailQuote.version }}
              <XhButton v-if="detailQuote.previousVersionId" class="inline-link" variant="subtle" @click="openPreviousVersion">
                <XhButtonLabel>{{ t('crm.quote.previousVersion') }}</XhButtonLabel>
              </XhButton>
            </p>
            <p>{{ t('crm.quote.status') }}: {{ statusLabel(detailQuote.status) }}</p>
            <p>{{ t('crm.quote.date') }}: {{ new Date(detailQuote.issueDate).toLocaleDateString() }}</p>
            <p>{{ t('crm.quote.total') }}: {{ detailQuote.grandTotal.toLocaleString() }} {{ detailQuote.currency }}</p>
            <p v-if="detailQuote.convertedOrderId">
              {{ t('crm.quote.convertedOrder') }}: #{{ detailQuote.convertedOrderId }}
            </p>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{{ t('crm.quote.descriptionLine') }}</th>
                  <th>{{ t('crm.quote.quantity') }}</th>
                  <th>{{ t('crm.quote.unitPrice') }}</th>
                  <th>{{ t('crm.quote.total') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="line in detailLines" :key="line.basicId">
                  <td>{{ line.description }}</td>
                  <td>{{ line.quantity }} {{ line.unit || '' }}</td>
                  <td>{{ line.unitPrice.toLocaleString() }}</td>
                  <td>{{ line.lineTotal.toLocaleString() }}</td>
                </tr>
                <tr v-if="!detailLines.length">
                  <td colspan="4" class="empty">
                    {{ t('crm.quote.noLines') }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-if="detailQuote.termsAndConditions" class="detail-notes">
            {{ t('crm.quote.terms') }}: {{ detailQuote.termsAndConditions }}
          </p>
          <p v-if="detailQuote.notes" class="detail-notes">
            {{ t('crm.quote.notes') }}: {{ detailQuote.notes }}
          </p>
          <footer class="detail-actions">
            <XhButton v-if="canCreate && [CrmQuoteStatus.Sent, CrmQuoteStatus.Rejected, CrmQuoteStatus.Expired].includes(detailQuote.status)" variant="outline" @click="openRevision">
              <XhButtonLabel>{{ t('crm.quote.createRevision') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </template>
      </section>
    </div>
    <div v-if="reviseOpen" class="modal-backdrop" @click.self="reviseOpen = false">
      <section class="editor revision-editor">
        <header>
          <h2>{{ t('crm.quote.revisionTitle') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.quote.cancel')" @click="reviseOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="saveRevision">
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.issueDate') }} *</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="reviseForm.issueDate" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.validUntil') }}</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="reviseForm.validUntil" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field full-width">
            <XhFieldLabel>{{ t('crm.quote.terms') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="reviseForm.termsAndConditions" type="textarea" :rows="2" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field full-width">
            <XhFieldLabel>{{ t('crm.quote.notes') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="reviseForm.notes" type="textarea" :rows="2" />
            </XhFieldControl>
          </XhFieldRoot>
          <div class="revision-lines full-width">
            <strong>{{ t('crm.quote.revisionLines') }} ({{ reviseLines.length }})</strong>
            <div v-for="(line, index) in reviseLines" :key="index" class="revision-line">
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.quote.descriptionLine') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput v-model:value="line.description" required :max-length="500" />
                </XhFieldControl>
              </XhFieldRoot>
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.quote.quantity') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput v-model:value="line.quantity" inputmode="numeric" required />
                </XhFieldControl>
              </XhFieldRoot>
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.quote.unitPrice') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput v-model:value="line.unitPrice" inputmode="numeric" required />
                </XhFieldControl>
              </XhFieldRoot>
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.quote.discountRate') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput v-model:value="line.discountRate" inputmode="numeric" />
                </XhFieldControl>
              </XhFieldRoot>
              <XhFieldRoot class="form-field">
                <XhFieldLabel>{{ t('crm.quote.taxRate') }}</XhFieldLabel>
                <XhFieldControl>
                  <XInput v-model:value="line.taxRate" inputmode="numeric" />
                </XhFieldControl>
              </XhFieldRoot>
            </div>
          </div>
          <footer>
            <XhButton variant="subtle" @click="reviseOpen = false">
              <XhButtonLabel>{{ t('crm.quote.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="revising">
              <XhButtonLabel>{{ revising ? t('crm.quote.saving') : t('crm.quote.saveRevision') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
    <div v-if="createOpen" class="modal-backdrop" @click.self="createOpen = false">
      <section class="editor">
        <header>
          <h2>{{ t('crm.quote.newTitle') }}</h2>
          <XhButton variant="subtle" :aria-label="t('crm.quote.cancel')" @click="createOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="createQuote">
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.customerId') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.customerId" required inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.issueDate') }} *</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="form.issueDate" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.validUntil') }}</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="form.validUntil" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.currency') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.currency" :max-length="3" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.descriptionLine') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.description" required :max-length="500" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.quantity') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.quantity" inputmode="numeric" required />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot class="form-field">
            <XhFieldLabel>{{ t('crm.quote.unitPrice') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.unitPrice" inputmode="numeric" required />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="createOpen = false">
              <XhButtonLabel>{{ t('crm.quote.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="saving">
              <XhButtonLabel>{{ saving ? t('crm.quote.saving') : t('crm.quote.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
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
  border-radius: var(--radius-lg);
  background: hsl(var(--card));
}
table {
  width: 100%;
  border-collapse: collapse;
  min-width: 800px;
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
  width: min(720px, 100%);
  max-height: 90vh;
  overflow: auto;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius-xl);
  padding: 20px;
  background: hsl(var(--card));
}
.quote-detail-editor {
  width: min(900px, 100%);
}
.detail-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}
.revision-editor {
  width: min(900px, 100%);
}
.full-width {
  grid-column: 1/-1;
}
.revision-lines {
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  padding: 12px;
}
.revision-line {
  display: grid;
  grid-template-columns: 2fr repeat(4, minmax(90px, 1fr));
  gap: 8px;
  padding: 10px 0;
  border-top: 1px solid hsl(var(--border));
}
.revision-line .form-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  font-size: 11px;
  color: hsl(var(--muted-foreground));
}
.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px 16px;
  font-size: 12px;
}
.inline-link {
  color: hsl(var(--primary));
  text-decoration: underline;
}
.detail-grid p,
.detail-notes {
  margin: 5px 0;
  color: hsl(var(--muted-foreground));
}
.quote-detail-editor .table-wrap {
  margin-top: 12px;
}
.editor form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 13px;
  margin-top: 18px;
}
.editor .form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  font-size: 12px;
  color: hsl(var(--muted-foreground));
}
.editor form footer {
  grid-column: 1/-1;
  justify-content: flex-end;
}
@media (max-width: 680px) {
  .crm-workflow-page {
    padding: 12px;
  }
  .editor form {
    grid-template-columns: 1fr;
  }
  .editor form footer {
    grid-column: auto;
  }
  .revision-line {
    grid-template-columns: 1fr 1fr;
  }
  .full-width {
    grid-column: auto;
  }
  .detail-grid {
    grid-template-columns: 1fr;
  }
}
</style>
