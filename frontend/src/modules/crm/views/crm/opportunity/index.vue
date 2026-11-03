<script setup lang="ts">
import type { CrmOpportunityCreateDto, CrmOpportunityListItemDto, CrmOpportunityUpdateDto } from '@/api'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, CrmOpportunityStage, crmOpportunityApi } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmOpportunityPage' })

const { hasPermission } = usePermission()
const { t } = useI18n()
const loading = ref(false)
const submitting = ref(false)
const keyword = ref('')
const stageFilter = ref<CrmOpportunityStage | ''>('')
const rows = ref<CrmOpportunityListItemDto[]>([])
const pageIndex = ref(1)
const pageSize = 20
const totalCount = ref(0)
const editorOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive({
  customerId: '',
  primaryContactId: '',
  name: '',
  stage: CrmOpportunityStage.Qualification,
  amount: '0',
  currency: 'USD',
  expectedCloseDate: '',
  probability: '',
  ownerUserId: '',
  lostReason: '',
})

const canCreate = computed(() => hasPermission('crm_opportunities.create'))
const canUpdate = computed(() => hasPermission('crm_opportunities.update'))
const canDelete = computed(() => hasPermission('crm_opportunities.delete'))
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function stageLabel(stage: CrmOpportunityStage | number) {
  switch (stage) {
    case CrmOpportunityStage.NeedsAnalysis: return t('crm.opportunity.needsAnalysis')
    case CrmOpportunityStage.Proposal: return t('crm.opportunity.proposal')
    case CrmOpportunityStage.Negotiation: return t('crm.opportunity.negotiation')
    case CrmOpportunityStage.Won: return t('crm.opportunity.won')
    case CrmOpportunityStage.Lost: return t('crm.opportunity.lost')
    default: return t('crm.opportunity.qualification')
  }
}

function resetForm() {
  Object.assign(form, {
    customerId: '',
    primaryContactId: '',
    name: '',
    stage: CrmOpportunityStage.Qualification,
    amount: '0',
    currency: 'USD',
    expectedCloseDate: '',
    probability: '',
    ownerUserId: '',
    lostReason: '',
  })
}

async function loadPage() {
  loading.value = true
  try {
    const request = createPageRequest({ page: { pageIndex: pageIndex.value, pageSize } })
    const result = await crmOpportunityApi.page({
      ...request,
      keyword: keyword.value.trim() || null,
      stage: stageFilter.value === '' ? null : stageFilter.value,
    })
    rows.value = result.items
    totalCount.value = result.page.totalCount
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.opportunity.loadError'))
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

async function openEdit(row: CrmOpportunityListItemDto) {
  if (!canUpdate.value) return
  loading.value = true
  try {
    const detail = await crmOpportunityApi.detail(row.basicId)
    if (!detail) {
      toast.warning(t('crm.opportunity.notFound'))
      return
    }
    resetForm()
    editingId.value = detail.basicId
    Object.assign(form, {
      customerId: detail.customerId,
      primaryContactId: detail.primaryContactId ?? '',
      name: detail.name,
      stage: detail.stage,
      amount: String(detail.amount),
      currency: detail.currency,
      expectedCloseDate: detail.expectedCloseDate ? detail.expectedCloseDate.slice(0, 10) : '',
      probability: detail.probability == null ? '' : String(detail.probability),
      ownerUserId: detail.ownerUserId ?? '',
      lostReason: detail.lostReason ?? '',
    })
    editorOpen.value = true
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.opportunity.detailError'))
  }
  finally {
    loading.value = false
  }
}

async function save() {
  if (!form.name.trim()) {
    toast.warning(t('crm.opportunity.nameRequired'))
    return
  }
  if (!form.customerId.trim()) {
    toast.warning(t('crm.opportunity.customerRequired'))
    return
  }
  const amount = Number(form.amount)
  if (!Number.isFinite(amount) || amount < 0) {
    toast.warning(t('crm.opportunity.amountInvalid'))
    return
  }
  const probability = form.probability.trim() ? Number(form.probability) : null
  if (probability != null && (!Number.isInteger(probability) || probability < 0 || probability > 100)) {
    toast.warning(t('crm.opportunity.probabilityInvalid'))
    return
  }
  if (form.stage === CrmOpportunityStage.Lost && !form.lostReason.trim()) {
    toast.warning(t('crm.opportunity.lostReasonRequired'))
    return
  }

  submitting.value = true
  try {
    const common = {
      customerId: form.customerId.trim(),
      primaryContactId: form.primaryContactId.trim() || null,
      name: form.name.trim(),
      stage: form.stage,
      amount,
      currency: form.currency.trim().toUpperCase(),
      expectedCloseDate: form.expectedCloseDate ? `${form.expectedCloseDate}T00:00:00` : null,
      probability,
      ownerUserId: form.ownerUserId.trim() || null,
      lostReason: form.lostReason.trim() || null,
    }
    if (editingId.value) {
      const input: CrmOpportunityUpdateDto = { ...common, basicId: editingId.value }
      await crmOpportunityApi.update(input)
      toast.success(t('crm.opportunity.updateSuccess'))
    }
    else {
      const input: CrmOpportunityCreateDto = common
      await crmOpportunityApi.create(input)
      toast.success(t('crm.opportunity.createSuccess'))
    }
    editorOpen.value = false
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.opportunity.saveError'))
  }
  finally {
    submitting.value = false
  }
}

async function remove(row: CrmOpportunityListItemDto) {
  if (!canDelete.value) return
  if (!window.confirm(t('crm.opportunity.confirmDelete', { name: row.name }))) return
  try {
    await crmOpportunityApi.delete(row.basicId)
    toast.success(t('crm.opportunity.deleteSuccess'))
    await loadPage()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.opportunity.deleteError'))
  }
}

onMounted(loadPage)
</script>

<template>
  <main class="crm-opportunity-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.opportunity.title') }}</h1>
        <p>{{ t('crm.opportunity.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCreate">
        {{ t('crm.opportunity.create') }}
      </button>
    </header>

    <section class="toolbar" :aria-label="t('crm.opportunity.title')">
      <input
        v-model="keyword"
        :aria-label="t('crm.opportunity.search')"
        :placeholder="t('crm.opportunity.search')"
        @keydown.enter="search"
      >
      <select v-model="stageFilter" :aria-label="t('crm.opportunity.stage')">
        <option value="">{{ t('crm.opportunity.allStages') }}</option>
        <option :value="CrmOpportunityStage.Qualification">{{ t('crm.opportunity.qualification') }}</option>
        <option :value="CrmOpportunityStage.NeedsAnalysis">{{ t('crm.opportunity.needsAnalysis') }}</option>
        <option :value="CrmOpportunityStage.Proposal">{{ t('crm.opportunity.proposal') }}</option>
        <option :value="CrmOpportunityStage.Negotiation">{{ t('crm.opportunity.negotiation') }}</option>
        <option :value="CrmOpportunityStage.Won">{{ t('crm.opportunity.won') }}</option>
        <option :value="CrmOpportunityStage.Lost">{{ t('crm.opportunity.lost') }}</option>
      </select>
      <button type="button" @click="search">{{ t('crm.opportunity.query') }}</button>
      <button type="button" :disabled="loading" @click="loadPage">{{ t('crm.opportunity.refresh') }}</button>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">{{ t('crm.opportunity.loading') }}</div>
      <div v-else-if="rows.length === 0" class="empty-state">{{ t('crm.opportunity.empty') }}</div>
      <table v-else>
        <thead>
          <tr>
            <th>{{ t('crm.opportunity.name') }}</th>
            <th>{{ t('crm.opportunity.customerId') }}</th>
            <th>{{ t('crm.opportunity.stage') }}</th>
            <th>{{ t('crm.opportunity.amount') }}</th>
            <th>{{ t('crm.opportunity.probability') }}</th>
            <th>{{ t('crm.opportunity.expectedCloseDate') }}</th>
            <th>{{ t('crm.opportunity.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.basicId">
            <td><strong>{{ row.name }}</strong></td>
            <td>{{ row.customerId }}</td>
            <td><span class="stage-pill" :data-stage="row.stage">{{ stageLabel(row.stage) }}</span></td>
            <td>{{ row.amount.toLocaleString(undefined, { maximumFractionDigits: 2 }) }} {{ row.currency }}</td>
            <td>{{ row.probability == null ? '—' : `${row.probability}%` }}</td>
            <td>{{ row.expectedCloseDate ? new Date(row.expectedCloseDate).toLocaleDateString() : '—' }}</td>
            <td class="actions">
              <button v-if="canUpdate" type="button" @click="openEdit(row)">{{ t('crm.opportunity.edit') }}</button>
              <button v-if="canDelete" class="danger-action" type="button" @click="remove(row)">{{ t('crm.opportunity.delete') }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.opportunity.total', { count: totalCount }) }} · {{ t('crm.opportunity.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <button type="button" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">{{ t('crm.opportunity.previous') }}</button>
        <button type="button" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">{{ t('crm.opportunity.next') }}</button>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.opportunity.editTitle') : t('crm.opportunity.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.opportunity.editTitle') : t('crm.opportunity.newTitle') }}</h2>
          <button type="button" :aria-label="t('common.actions.close')" @click="editorOpen = false">×</button>
        </header>
        <form @submit.prevent="save">
          <label>{{ t('crm.opportunity.name') }} *<input v-model="form.name" required maxlength="200"></label>
          <label>{{ t('crm.opportunity.customerId') }} *<input v-model="form.customerId" required inputmode="numeric"></label>
          <label>{{ t('crm.opportunity.contactId') }}<input v-model="form.primaryContactId" inputmode="numeric"></label>
          <label>{{ t('crm.opportunity.stage') }}
            <select v-model.number="form.stage">
              <option :value="CrmOpportunityStage.Qualification">{{ t('crm.opportunity.qualification') }}</option>
              <option :value="CrmOpportunityStage.NeedsAnalysis">{{ t('crm.opportunity.needsAnalysis') }}</option>
              <option :value="CrmOpportunityStage.Proposal">{{ t('crm.opportunity.proposal') }}</option>
              <option :value="CrmOpportunityStage.Negotiation">{{ t('crm.opportunity.negotiation') }}</option>
              <option :value="CrmOpportunityStage.Won">{{ t('crm.opportunity.won') }}</option>
              <option :value="CrmOpportunityStage.Lost">{{ t('crm.opportunity.lost') }}</option>
            </select>
          </label>
          <label>{{ t('crm.opportunity.amount') }} *<input v-model="form.amount" type="number" min="0" step="0.01" required></label>
          <label>{{ t('crm.opportunity.currency') }} *<input v-model="form.currency" minlength="3" maxlength="3" required></label>
          <label>{{ t('crm.opportunity.expectedCloseDate') }}<input v-model="form.expectedCloseDate" type="date"></label>
          <label>{{ t('crm.opportunity.probability') }}<input v-model="form.probability" type="number" min="0" max="100" step="1"></label>
          <label>{{ t('crm.opportunity.ownerId') }}<input v-model="form.ownerUserId" inputmode="numeric"></label>
          <label v-if="form.stage === CrmOpportunityStage.Lost" class="wide">{{ t('crm.opportunity.lostReason') }} *<textarea v-model="form.lostReason" rows="2" maxlength="500" required /></label>
          <footer>
            <button type="button" @click="editorOpen = false">{{ t('crm.opportunity.cancel') }}</button>
            <button class="primary-button" type="submit" :disabled="submitting">
              {{ submitting ? t('crm.opportunity.saving') : t('crm.opportunity.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-opportunity-page { display: flex; flex-direction: column; gap: 16px; min-height: 100%; padding: 20px; color: hsl(var(--foreground)); }
.page-header, .toolbar, .pagination, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.page-header h1, .editor h2 { margin: 0; font-size: 20px; font-weight: 650; }
.page-header p { margin: 5px 0 0; color: hsl(var(--muted-foreground)); font-size: 12px; }
button, input, select, textarea { font: inherit; }
button { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 7px 11px; background: hsl(var(--card)); cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.primary-button { border-color: hsl(var(--primary)); background: hsl(var(--primary)); color: hsl(var(--primary-foreground)); }
.toolbar { justify-content: flex-start; flex-wrap: wrap; }
.toolbar input { min-width: 240px; }
.toolbar input, .toolbar select, .editor input, .editor select, .editor textarea { border: 1px solid hsl(var(--border)); border-radius: 7px; padding: 8px 10px; background: hsl(var(--card)); color: inherit; }
.table-wrap { overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 10px; background: hsl(var(--card)); }
table { width: 100%; border-collapse: collapse; min-width: 900px; text-align: left; font-size: 13px; }
th, td { padding: 11px 12px; border-bottom: 1px solid hsl(var(--border)); vertical-align: middle; }
th { color: hsl(var(--muted-foreground)); font-size: 11px; font-weight: 600; white-space: nowrap; }
.actions { white-space: nowrap; }
.actions button { margin-right: 5px; padding: 5px 8px; font-size: 11px; }
.danger-action { color: var(--xh-color-danger-600); }
.stage-pill { display: inline-block; padding: 3px 8px; border-radius: 999px; background: hsl(var(--muted)); font-size: 11px; white-space: nowrap; }
.stage-pill[data-stage="4"] { color: var(--xh-color-success-600); }
.stage-pill[data-stage="5"] { color: var(--xh-color-danger-600); }
.empty-state { padding: 44px 16px; text-align: center; color: hsl(var(--muted-foreground)); }
.pagination { color: hsl(var(--muted-foreground)); font-size: 12px; }
.pagination div { display: flex; gap: 6px; }
.modal-backdrop { position: fixed; z-index: 1000; inset: 0; display: grid; place-items: center; padding: 20px; background: rgb(0 0 0 / 42%); }
.editor { width: min(760px, 100%); max-height: min(90vh, 900px); overflow: auto; border: 1px solid hsl(var(--border)); border-radius: 12px; padding: 20px; background: hsl(var(--card)); box-shadow: 0 20px 70px rgb(0 0 0 / 20%); }
.editor form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 13px; margin-top: 18px; }
.editor label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; color: hsl(var(--muted-foreground)); }
.editor label input, .editor label select, .editor label textarea { width: 100%; box-sizing: border-box; color: hsl(var(--foreground)); }
.editor .wide, .editor form footer { grid-column: 1 / -1; }
.editor form footer { justify-content: flex-end; }
@media (max-width: 680px) {
  .crm-opportunity-page { padding: 12px; }
  .editor form { grid-template-columns: 1fr; }
  .editor .wide, .editor form footer { grid-column: auto; }
}
</style>
