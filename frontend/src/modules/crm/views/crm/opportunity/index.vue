<script setup lang="ts">
import type { CrmOpportunityCreateDto, CrmOpportunityListItemDto, CrmOpportunityUpdateDto } from '@/api'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPageRequest, crmOpportunityApi, CrmOpportunityStage } from '@/api'
import { XDatePicker, XInput, XSelect } from '~/components'
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

const stageFilterOptions = computed(() => [
  { label: t('crm.opportunity.allStages'), value: '' },
  { label: t('crm.opportunity.qualification'), value: String(CrmOpportunityStage.Qualification) },
  { label: t('crm.opportunity.needsAnalysis'), value: String(CrmOpportunityStage.NeedsAnalysis) },
  { label: t('crm.opportunity.proposal'), value: String(CrmOpportunityStage.Proposal) },
  { label: t('crm.opportunity.negotiation'), value: String(CrmOpportunityStage.Negotiation) },
  { label: t('crm.opportunity.won'), value: String(CrmOpportunityStage.Won) },
  { label: t('crm.opportunity.lost'), value: String(CrmOpportunityStage.Lost) },
])
// XSelect 收发字符串选项；这里把值还原成 number | ''，保持原 v-model.number 的数字枚举语义。
const stageFilterValue = computed({
  get: () => String(stageFilter.value),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    stageFilter.value = next == null || next === '' ? '' : Number(next) as CrmOpportunityStage
  },
})
const opportunityStageOptions = computed(() => [
  { label: t('crm.opportunity.qualification'), value: String(CrmOpportunityStage.Qualification) },
  { label: t('crm.opportunity.needsAnalysis'), value: String(CrmOpportunityStage.NeedsAnalysis) },
  { label: t('crm.opportunity.proposal'), value: String(CrmOpportunityStage.Proposal) },
  { label: t('crm.opportunity.negotiation'), value: String(CrmOpportunityStage.Negotiation) },
  { label: t('crm.opportunity.won'), value: String(CrmOpportunityStage.Won) },
  { label: t('crm.opportunity.lost'), value: String(CrmOpportunityStage.Lost) },
])
const formStageValue = computed({
  get: () => String(form.stage),
  set: (value: string | number | Array<string | number> | null) => {
    const next = Array.isArray(value) ? value[0] : value
    form.stage = next == null ? CrmOpportunityStage.Qualification : Number(next) as CrmOpportunityStage
  },
})
function toLocalDateKey(value: number): string {
  const date = new Date(value)
  const pad = (part: number) => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}
// XDatePicker 用时间戳收发；form.expectedCloseDate 仍是提交逻辑使用的 'YYYY-MM-DD'。
const expectedCloseDateValue = computed({
  get: () => form.expectedCloseDate ? new Date(`${form.expectedCloseDate}T00:00:00`).getTime() : null,
  set: (value: number | null) => {
    form.expectedCloseDate = value == null ? '' : toLocalDateKey(value)
  },
})

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
  if (!canCreate.value)
    return
  resetForm()
  editingId.value = null
  editorOpen.value = true
}

async function openEdit(row: CrmOpportunityListItemDto) {
  if (!canUpdate.value)
    return
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
  if (!canDelete.value)
    return
  if (!window.confirm(t('crm.opportunity.confirmDelete', { name: row.name })))
    return
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
      <XhButton v-if="canCreate" variant="outline" @click="openCreate">
        <XhButtonLabel>{{ t('crm.opportunity.create') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="toolbar" :aria-label="t('crm.opportunity.title')">
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.opportunity.search') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XInput
            v-model:value="keyword"
            :placeholder="t('crm.opportunity.search')"
            @enter="search"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="toolbar-field">
        <XhFieldLabel class="sr-only">
          {{ t('crm.opportunity.stage') }}
        </XhFieldLabel>
        <XhFieldControl>
          <XSelect
            v-model:value="stageFilterValue"
            :options="stageFilterOptions"
            :aria-label="t('crm.opportunity.stage')"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="search">
        <XhButtonLabel>{{ t('crm.opportunity.query') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" :disabled="loading" @click="loadPage">
        <XhButtonLabel>{{ t('crm.opportunity.refresh') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="table-wrap" aria-live="polite">
      <div v-if="loading && rows.length === 0" class="empty-state">
        {{ t('crm.opportunity.loading') }}
      </div>
      <div v-else-if="rows.length === 0" class="empty-state">
        {{ t('crm.opportunity.empty') }}
      </div>
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
              <XhButton v-if="canUpdate" variant="outline" @click="openEdit(row)">
                <XhButtonLabel>{{ t('crm.opportunity.edit') }}</XhButtonLabel>
              </XhButton>
              <XhButton v-if="canDelete" variant="outline" tone="danger" @click="remove(row)">
                <XhButtonLabel>{{ t('crm.opportunity.delete') }}</XhButtonLabel>
              </XhButton>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <footer class="pagination">
      <span>{{ t('crm.opportunity.total', { count: totalCount }) }} · {{ t('crm.opportunity.page', { current: pageIndex, total: totalPages }) }}</span>
      <div>
        <XhButton variant="subtle" :disabled="pageIndex <= 1 || loading" @click="pageIndex--; loadPage()">
          <XhButtonLabel>{{ t('crm.opportunity.previous') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="pageIndex >= totalPages || loading" @click="pageIndex++; loadPage()">
          <XhButtonLabel>{{ t('crm.opportunity.next') }}</XhButtonLabel>
        </XhButton>
      </div>
    </footer>

    <div v-if="editorOpen" class="modal-backdrop" @click.self="editorOpen = false">
      <section class="editor" role="dialog" aria-modal="true" :aria-label="editingId ? t('crm.opportunity.editTitle') : t('crm.opportunity.newTitle')">
        <header>
          <h2>{{ editingId ? t('crm.opportunity.editTitle') : t('crm.opportunity.newTitle') }}</h2>
          <XhButton variant="subtle" size="sm" :aria-label="t('common.actions.close')" @click="editorOpen = false">
            <XhButtonLabel>×</XhButtonLabel>
          </XhButton>
        </header>
        <form @submit.prevent="save">
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.name') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.name" required :max-length="200" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.customerId') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.customerId" required inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.contactId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.primaryContactId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.stage') }}</XhFieldLabel>
            <XhFieldControl>
              <XSelect v-model:value="formStageValue" :options="opportunityStageOptions" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.amount') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.amount" required inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.currency') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.currency" required minlength="3" :max-length="3" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.expectedCloseDate') }}</XhFieldLabel>
            <XhFieldControl>
              <XDatePicker v-model:value="expectedCloseDateValue" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.probability') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.probability" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot>
            <XhFieldLabel>{{ t('crm.opportunity.ownerId') }}</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.ownerUserId" inputmode="numeric" />
            </XhFieldControl>
          </XhFieldRoot>
          <XhFieldRoot v-if="form.stage === CrmOpportunityStage.Lost" class="wide">
            <XhFieldLabel>{{ t('crm.opportunity.lostReason') }} *</XhFieldLabel>
            <XhFieldControl>
              <XInput v-model:value="form.lostReason" type="textarea" :rows="2" :max-length="500" required />
            </XhFieldControl>
          </XhFieldRoot>
          <footer>
            <XhButton variant="subtle" @click="editorOpen = false">
              <XhButtonLabel>{{ t('crm.opportunity.cancel') }}</XhButtonLabel>
            </XhButton>
            <XhButton type="submit" variant="outline" :disabled="submitting">
              <XhButtonLabel>{{ submitting ? t('crm.opportunity.saving') : t('crm.opportunity.save') }}</XhButtonLabel>
            </XhButton>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-opportunity-page {
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
.toolbar-field {
  min-width: 240px;
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
  min-width: 900px;
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
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
.stage-pill {
  display: inline-block;
  padding: 3px 8px;
  border-radius: var(--radius-full);
  background: hsl(var(--muted));
  font-size: 11px;
  white-space: nowrap;
}
.stage-pill[data-stage='4'] {
  color: hsl(var(--success));
}
.stage-pill[data-stage='5'] {
  color: hsl(var(--destructive));
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
  .crm-opportunity-page {
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
