<script setup lang="ts">
// 本页与其余 CRM 管理页一致沿用浏览器原生确认框；全仓 no-alert 统一替换前在此局部豁免。
/* eslint-disable no-alert */
import type { CrmProspectingProviderGroup } from './provider-catalog'
import type { CrmProspectingBulkAction } from './view-types'
import type {
  CrmProspectingBulkResultDto,
  CrmProspectingCampaignDto,
  CrmProspectingCandidateDto,
  CrmProspectingCandidateReviewDetailDto,
  CrmProspectingProviderCatalogItemDto,
  CrmProspectingRunDto,
  CrmProspectingRunMonitorDto,
  CrmProspectingStrategyDto,
  ExportColumn,
} from '@/api'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  createPageRequest,
  crmProspectingApi,
  CrmProspectingBulkOutcome,
  CrmProspectingCandidateStatus,
  CrmProspectingRunStatus,
  CrmProspectingStrategyApprovalStatus,
  ExportFormat,
  ExportScope,
  exportTaskApi,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'
import BulkActionBar from './BulkActionBar.vue'
import CandidateTable from './CandidateTable.vue'
import { useProspectingLabels } from './prospecting-labels'
import {
  CRM_PROSPECTING_PROVIDER_FALLBACK_CATALOG,
  CRM_PROSPECTING_PROVIDER_GROUPS,
} from './provider-catalog'
import ProviderCredentialsPanel from './ProviderCredentialsPanel.vue'
import ReviewDetailPanel from './ReviewDetailPanel.vue'
import RunMonitorPanel from './RunMonitorPanel.vue'
import SchedulePanel from './SchedulePanel.vue'
import SuperSearchPreviewPanel from './SuperSearchPreviewPanel.vue'
import { useRunMonitorPolling } from './useRunMonitorPolling'

defineOptions({ name: 'CrmProspectingPage' })

const { t } = useI18n()
const { candidateStatus } = useProspectingLabels()
const { hasPermission } = usePermission()
const loading = ref(false)
const submitting = ref(false)
const campaigns = ref<CrmProspectingCampaignDto[]>([])
const selectedCampaignId = ref<string>('')
const strategies = ref<CrmProspectingStrategyDto[]>([])
const runs = ref<CrmProspectingRunDto[]>([])
const candidates = ref<CrmProspectingCandidateDto[]>([])
const reviewDetail = ref<CrmProspectingCandidateReviewDetailDto | null>(null)
const reviewDetailLoading = ref(false)
const selectedRunId = ref<string>('')
const campaignDialog = ref(false)
const strategyDialog = ref(false)
const campaignForm = ref({ name: '', objective: '', productScope: '', targetMarkets: '', idealCustomerProfile: '' })
const strategyForm = ref({ name: 'Default search strategy', searchObjective: '', providerPlanJson: '[]', excludedDomainsJson: '[]' })
const providerSelection = ref<string[]>([])
const providerCatalog = ref<CrmProspectingProviderCatalogItemDto[]>(CRM_PROSPECTING_PROVIDER_FALLBACK_CATALOG)
const catalogIsFallback = ref(true)
const catalogRegisteredCount = ref(0)
const catalogTotalCount = ref(CRM_PROSPECTING_PROVIDER_FALLBACK_CATALOG.length)
const editingCampaignId = ref<string>('')
const runDetail = ref<CrmProspectingRunDto | null>(null)
const runMonitor = ref<CrmProspectingRunMonitorDto | null>(null)
const candidateStatusFilter = ref<number | ''>('')
// The review queue pages server-side: one Run's rows are partitioned by CreatedTime DESC,
// BasicId DESC, so consecutive pages never repeat or drop a row.
const candidatePageIndex = ref(1)
const candidatePageSize = 20
const candidateTotal = ref(0)
const exporting = ref(false)
const candidateShortlistFilter = ref<'' | 'true' | 'false'>('')
const selectedCandidateIds = ref<string[]>([])
const bulkBusy = ref(false)
const bulkResult = ref<CrmProspectingBulkResultDto | null>(null)
const shortlistedQuery = computed(() => candidateShortlistFilter.value === ''
  ? undefined
  : candidateShortlistFilter.value === 'true')

const selectedCampaign = computed(() => campaigns.value.find(item => String(item.basicId) === selectedCampaignId.value) ?? null)
const canCreate = computed(() => hasPermission('crm_prospecting.create'))
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))
const canCreateLead = computed(() => hasPermission('crm_prospecting.convert_lead'))
const canExport = computed(() => hasPermission('crm_prospecting.export'))
const candidateTotalPages = computed(() => Math.max(1, Math.ceil(candidateTotal.value / candidatePageSize)))

function campaignStatus(value: number) {
  return [t('crm.prospecting.draft'), t('crm.prospecting.active'), t('crm.prospecting.paused'), t('crm.prospecting.completed'), t('crm.prospecting.archived')][value] ?? String(value)
}
function strategyStatus(value: number) {
  return [t('crm.prospecting.strategyDraft'), t('crm.prospecting.pendingApproval'), t('crm.prospecting.approved'), t('crm.prospecting.rejected'), t('crm.prospecting.revoked')][value] ?? String(value)
}
function runStatus(value: number) {
  return [t('crm.prospecting.queued'), t('crm.prospecting.running'), t('crm.prospecting.succeeded'), t('crm.prospecting.partiallySucceeded'), t('crm.prospecting.failed'), t('crm.prospecting.cancelRequested'), t('crm.prospecting.cancelled'), t('crm.prospecting.waitingForExtractions'), t('crm.prospecting.runPaused')][value] ?? String(value)
}

/**
 * The backend catalog is the single source of truth. On failure we keep the static fallback
 * and mark the status as unknown instead of claiming providers are enabled/disabled.
 */
async function loadProviderCatalog() {
  try {
    const catalog = await crmProspectingApi.providerCatalog()
    if (!Array.isArray(catalog?.providers) || catalog.providers.length === 0)
      throw new Error('empty provider catalog')
    providerCatalog.value = catalog.providers
    catalogRegisteredCount.value = catalog.registeredCount
    catalogTotalCount.value = catalog.totalCount
    catalogIsFallback.value = false
  }
  catch {
    providerCatalog.value = CRM_PROSPECTING_PROVIDER_FALLBACK_CATALOG
    catalogIsFallback.value = true
  }
}

async function loadCampaigns() {
  loading.value = true
  try {
    campaigns.value = await crmProspectingApi.campaigns()
    if (!campaigns.value.some(item => String(item.basicId) === selectedCampaignId.value))
      selectedCampaignId.value = campaigns.value[0] ? String(campaigns.value[0].basicId) : ''
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
  finally {
    loading.value = false
  }
}

async function loadWorkspace() {
  if (!selectedCampaignId.value) {
    strategies.value = []
    runs.value = []
    candidates.value = []
    candidateTotal.value = 0
    selectedRunId.value = ''
    runDetail.value = null
    runMonitor.value = null
    return
  }
  loading.value = true
  try {
    const [strategyRows, runRows] = await Promise.all([
      crmProspectingApi.strategies(selectedCampaignId.value),
      crmProspectingApi.runs(selectedCampaignId.value),
    ])
    strategies.value = strategyRows
    runs.value = runRows
    if (!runRows.some(item => String(item.basicId) === selectedRunId.value))
      selectedRunId.value = runRows[0] ? String(runRows[0].basicId) : ''
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
  finally {
    loading.value = false
  }
}

async function loadCandidates() {
  if (!selectedRunId.value) {
    candidates.value = []
    candidateTotal.value = 0
    return
  }
  const request = {
    runId: selectedRunId.value,
    status: candidateStatusFilter.value === '' ? undefined : candidateStatusFilter.value,
    shortlisted: shortlistedQuery.value,
    pageIndex: candidatePageIndex.value,
  }
  try {
    const result = await crmProspectingApi.candidatePage({
      ...createPageRequest({ page: { pageIndex: request.pageIndex, pageSize: candidatePageSize } }),
      runId: request.runId,
      status: request.status,
      shortlisted: request.shortlisted,
    })
    // Drop responses for a run, filter or page the user already left, so a slow page
    // can never repaint the table after the next navigation.
    if (request.runId !== selectedRunId.value
      || request.status !== (candidateStatusFilter.value === '' ? undefined : candidateStatusFilter.value)
      || request.shortlisted !== shortlistedQuery.value
      || request.pageIndex !== candidatePageIndex.value) {
      return
    }
    if (result.items.length === 0 && request.pageIndex > 1) {
      // The last page emptied out (rows reviewed away): fall back to page 1 once.
      candidatePageIndex.value = 1
      await loadCandidates()
      return
    }
    candidates.value = result.items
    candidateTotal.value = result.page.totalCount
  }
  catch (error) {
    candidates.value = []
    toast.danger((error as Error)?.message || t('crm.prospecting.candidateLoadError'))
  }
}

function gotoCandidatePage(pageIndex: number) {
  const next = Math.min(Math.max(1, pageIndex), candidateTotalPages.value)
  if (next === candidatePageIndex.value)
    return
  candidatePageIndex.value = next
  void loadCandidates()
}

/** Export covers the whole filtered result set (SearchResult), not just the visible page. */
function candidateQuerySnapshot() {
  return {
    ...createPageRequest({ page: { pageIndex: 1, pageSize: candidatePageSize } }),
    runId: selectedRunId.value,
    status: candidateStatusFilter.value === '' ? null : candidateStatusFilter.value,
    shortlisted: candidateShortlistFilter.value === '' ? null : candidateShortlistFilter.value === 'true',
  }
}

function candidateExportColumns(): ExportColumn[] {
  const statusValueMap: Record<string, string> = {}
  for (const status of [0, 1, 2, 3, 4, 5])
    statusValueMap[String(status)] = candidateStatus(status)
  return [
    { key: 'companyName', title: t('crm.prospecting.company') },
    { key: 'contactName', title: t('crm.prospecting.contact') },
    { key: 'businessEmail', title: t('crm.prospecting.email') },
    { key: 'website', title: t('crm.prospecting.website') },
    { key: 'source', title: t('crm.prospecting.source') },
    { key: 'status', title: t('crm.prospecting.status'), valueMap: statusValueMap },
    { key: 'duplicateReason', title: t('crm.prospecting.duplicateReason') },
  ]
}

async function exportCandidates() {
  if (!selectedRunId.value || !canExport.value || exporting.value)
    return
  exporting.value = true
  try {
    await exportTaskApi.submit({
      businessType: 'crm.prospecting.candidate',
      taskName: t('crm.prospecting.exportTaskName', { runId: selectedRunId.value }),
      scope: ExportScope.SearchResult,
      format: ExportFormat.Csv,
      querySnapshot: JSON.stringify(candidateQuerySnapshot()),
      columns: candidateExportColumns(),
    })
    toast.success(t('crm.prospecting.exportQueued'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.exportError'))
  }
  finally {
    exporting.value = false
  }
}

/**
 * GET /CrmProspecting/Run + /CrmProspectingRunQuery/RunMonitor: counters, failureSummary and the
 * per-provider execution ledger refresh together; responses for a run the user already left are dropped.
 */
async function refreshRunDetail() {
  if (!selectedRunId.value) {
    runDetail.value = null
    runMonitor.value = null
    return
  }
  const activeId = selectedRunId.value
  const fallback = runs.value.find(item => String(item.basicId) === activeId) ?? null
  try {
    const [detail, monitor] = await Promise.all([
      crmProspectingApi.run(activeId),
      crmProspectingApi.runMonitor(activeId),
    ])
    if (activeId !== selectedRunId.value)
      return
    runDetail.value = detail ?? fallback
    runMonitor.value = monitor
    const index = runs.value.findIndex(item => String(item.basicId) === activeId)
    if (index >= 0 && detail)
      runs.value[index] = detail
  }
  catch {
    // Keep the last known state while polling; fall back to the list row only before the first load.
    if (activeId === selectedRunId.value && !runDetail.value)
      runDetail.value = fallback
  }
}

function selectRun(run: CrmProspectingRunDto) {
  const nextId = String(run.basicId)
  if (nextId === selectedRunId.value)
    void refreshRunDetail()
  else
    selectedRunId.value = nextId
}

watch(selectedCampaignId, () => {
  void loadWorkspace()
})
watch(selectedRunId, () => {
  // A selection belongs to one Run: never carry IDs into another Run's bulk action.
  candidatePageIndex.value = 1
  selectedCandidateIds.value = []
  bulkResult.value = null
  void loadCandidates()
  void refreshRunDetail()
})
watch(candidateStatusFilter, () => {
  candidatePageIndex.value = 1
  void loadCandidates()
})
watch(candidateShortlistFilter, () => {
  candidatePageIndex.value = 1
  selectedCandidateIds.value = []
  bulkResult.value = null
  void loadCandidates()
})

/** A Run keeps producing rows only while it is queued/running or waiting for async extractions. */
const shouldPollRun = computed(() => {
  if (!selectedRunId.value || !runDetail.value)
    return false
  const status = runDetail.value.status
  return status === CrmProspectingRunStatus.Queued
    || status === CrmProspectingRunStatus.Running
    || status === CrmProspectingRunStatus.WaitingForExtractions
})

useRunMonitorPolling({ shouldPoll: shouldPollRun, onTick: refreshRunDetail })

function openCampaignDialog(campaign?: CrmProspectingCampaignDto) {
  editingCampaignId.value = campaign ? String(campaign.basicId) : ''
  campaignForm.value = {
    name: campaign?.name ?? '',
    objective: campaign?.objective ?? '',
    productScope: campaign?.productScope ?? '',
    targetMarkets: campaign?.targetMarkets ?? '',
    idealCustomerProfile: campaign?.idealCustomerProfile ?? '',
  }
  campaignDialog.value = true
}
function editCurrentCampaign() {
  const campaign = selectedCampaign.value
  if (campaign)
    openCampaignDialog(campaign)
}
async function saveCampaign() {
  if (!campaignForm.value.name.trim()) {
    toast.warning(t('crm.prospecting.nameRequired'))
    return
  }
  const payload = {
    name: campaignForm.value.name.trim(),
    objective: campaignForm.value.objective || null,
    productScope: campaignForm.value.productScope || null,
    targetMarkets: campaignForm.value.targetMarkets || null,
    idealCustomerProfile: campaignForm.value.idealCustomerProfile || null,
  }
  const isEditing = editingCampaignId.value !== ''
  submitting.value = true
  try {
    if (isEditing) {
      await crmProspectingApi.updateCampaign({ ...payload, basicId: editingCampaignId.value })
    }
    else {
      const created = await crmProspectingApi.createCampaign(payload)
      selectedCampaignId.value = String(created.basicId)
    }
    campaignDialog.value = false
    await loadCampaigns()
    toast.success(t(isEditing ? 'crm.prospecting.campaignUpdated' : 'crm.prospecting.campaignCreated'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.saveError'))
  }
  finally {
    submitting.value = false
  }
}
function parseJsonArray(value: string): string[] | null {
  try {
    const parsed: unknown = JSON.parse(value)
    return Array.isArray(parsed) && parsed.every(item => typeof item === 'string')
      ? parsed as string[]
      : null
  }
  catch {
    return null
  }
}

function catalogFor(group: CrmProspectingProviderGroup) {
  return providerCatalog.value.filter(item => item.group === group)
}

function providerStatusLabel(item: CrmProspectingProviderCatalogItemDto) {
  if (catalogIsFallback.value)
    return t('crm.prospecting.providerStatusUnknown')
  if (item.isExecutable)
    return t('crm.prospecting.providerEnabled')
  if (item.capability === 'asynchronous')
    return t('crm.prospecting.providerAsyncOnly')
  return t('crm.prospecting.providerDisabled')
}

function providerCredentialLabel(item: CrmProspectingProviderCatalogItemDto) {
  return item.needsKey ? (item.keyOptional ? t('crm.prospecting.keyOptional') : t('crm.prospecting.keyRequired')) : t('crm.prospecting.keyFree')
}

function isProviderSelected(code: string) {
  return providerSelection.value.includes(code)
}

function toggleProvider(code: string, event: Event) {
  const checked = (event.target as HTMLInputElement).checked
  providerSelection.value = checked
    ? [...providerSelection.value, code]
    : providerSelection.value.filter(item => item !== code)
  strategyForm.value.providerPlanJson = JSON.stringify(providerSelection.value)
}

function onProviderPlanInput() {
  providerSelection.value = parseJsonArray(strategyForm.value.providerPlanJson) ?? []
}

function openStrategyDialog() {
  strategyForm.value = { name: 'Default search strategy', searchObjective: selectedCampaign.value?.objective || '', providerPlanJson: '[]', excludedDomainsJson: '[]' }
  providerSelection.value = []
  strategyDialog.value = true
}
async function saveStrategy() {
  if (!selectedCampaignId.value || !strategyForm.value.name.trim() || !strategyForm.value.searchObjective.trim()) {
    toast.warning(t('crm.prospecting.strategyRequired'))
    return
  }
  const providerPlan = parseJsonArray(strategyForm.value.providerPlanJson)
  const excludedDomains = parseJsonArray(strategyForm.value.excludedDomainsJson.trim() || '[]')
  if (!providerPlan || providerPlan.length === 0 || !excludedDomains) {
    toast.warning(t('crm.prospecting.providerJsonInvalid'))
    return
  }
  // Be honest: a plan made only of unregistered adapters will be skipped by the Run worker.
  if (!catalogIsFallback.value) {
    const unavailable = providerPlan.filter(code => !providerCatalog.value.some(item => item.code === code && item.isExecutable))
    if (unavailable.length === providerPlan.length && !window.confirm(t('crm.prospecting.confirmUnavailablePlan')))
      return
  }
  try {
    submitting.value = true
    await crmProspectingApi.createStrategyVersion({
      campaignId: selectedCampaignId.value,
      name: strategyForm.value.name.trim(),
      searchObjective: strategyForm.value.searchObjective.trim(),
      providerPlanJson: strategyForm.value.providerPlanJson,
      excludedDomainsJson: strategyForm.value.excludedDomainsJson,
    })
    strategyDialog.value = false
    await loadWorkspace()
    toast.success(t('crm.prospecting.strategyCreated'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.saveError'))
  }
  finally {
    submitting.value = false
  }
}
async function submitStrategy(strategy: CrmProspectingStrategyDto) {
  try {
    await crmProspectingApi.submitStrategyForApproval(strategy.basicId)
    await loadWorkspace()
    toast.success(t('crm.prospecting.submitted'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function decideStrategy(strategy: CrmProspectingStrategyDto, approve: boolean) {
  if (!window.confirm(approve ? t('crm.prospecting.confirmApproveStrategy') : t('crm.prospecting.confirmRejectStrategy')))
    return
  try {
    await crmProspectingApi.decideStrategy({ strategyId: strategy.basicId, approve })
    await loadWorkspace()
    toast.success(t('crm.prospecting.strategyDecisionSaved'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function activateStrategy(strategy: CrmProspectingStrategyDto) {
  if (!window.confirm(t('crm.prospecting.confirmActivate')))
    return
  try {
    await crmProspectingApi.activateStrategy(strategy.basicId)
    await loadWorkspace()
    await loadCampaigns()
    toast.success(t('crm.prospecting.strategyActivated'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function startRun() {
  if (!selectedCampaignId.value || !selectedCampaign.value?.activeStrategyVersion)
    return
  try {
    submitting.value = true
    await crmProspectingApi.startRun({
      campaignId: selectedCampaignId.value,
      requestKey: `prospecting-${selectedCampaignId.value}-${crypto.randomUUID()}`,
    })
    await loadWorkspace()
    toast.success(t('crm.prospecting.runQueued'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
  finally { submitting.value = false }
}
async function cancelRun(run: CrmProspectingRunDto) {
  if (!window.confirm(t('crm.prospecting.confirmCancelRun')))
    return
  try {
    await crmProspectingApi.cancelRun(run.basicId)
    await loadWorkspace()
    toast.success(t('crm.prospecting.cancelRequested'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function pauseRun(run: CrmProspectingRunDto) {
  try {
    await crmProspectingApi.pauseRun(run.basicId)
    await loadWorkspace()
    toast.success(t('crm.prospecting.runPausedNotice'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function resumeRun(run: CrmProspectingRunDto) {
  try {
    await crmProspectingApi.resumeRun(run.basicId)
    await loadWorkspace()
    toast.success(t('crm.prospecting.runResumedNotice'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}

async function openCandidateReview(candidate: CrmProspectingCandidateDto) {
  reviewDetailLoading.value = true
  reviewDetail.value = null
  try {
    reviewDetail.value = await crmProspectingApi.candidateReviewDetail(candidate.basicId)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.reviewDetailError'))
  }
  finally {
    reviewDetailLoading.value = false
  }
}

async function reviewCandidate(candidate: CrmProspectingCandidateDto, approve: boolean) {
  if (approve && candidate.status === CrmProspectingCandidateStatus.Duplicate && !window.confirm(t('crm.prospecting.confirmDuplicateOverride')))
    return
  try {
    await crmProspectingApi.reviewCandidate({
      candidateId: candidate.basicId,
      approve,
      overrideDuplicate: approve && candidate.status === CrmProspectingCandidateStatus.Duplicate,
    })
    await loadCandidates()
    toast.success(t('crm.prospecting.reviewSaved'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function convertCandidate(candidate: CrmProspectingCandidateDto) {
  if (!canCreateLead.value || !window.confirm(t('crm.prospecting.confirmConvert', { name: candidate.companyName })))
    return
  try {
    await crmProspectingApi.convertCandidate(candidate.basicId)
    await loadCandidates()
    toast.success(t('crm.prospecting.converted'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}

function toggleCandidate(id: string, checked: boolean) {
  const next = new Set(selectedCandidateIds.value)
  if (checked)
    next.add(id)
  else
    next.delete(id)
  selectedCandidateIds.value = [...next]
}

function toggleSelectAll(checked: boolean) {
  const next = new Set(selectedCandidateIds.value)
  for (const candidate of candidates.value) {
    const id = String(candidate.basicId)
    if (checked)
      next.add(id)
    else
      next.delete(id)
  }
  selectedCandidateIds.value = [...next]
}

function clearSelection() {
  selectedCandidateIds.value = []
  bulkResult.value = null
}

const bulkConfirmKeys: Partial<Record<CrmProspectingBulkAction, string>> = {
  approve: 'crm.prospecting.bulkConfirmApprove',
  reject: 'crm.prospecting.bulkConfirmReject',
  convert: 'crm.prospecting.bulkConfirmConvert',
}

async function runBulkAction(action: CrmProspectingBulkAction) {
  if (bulkBusy.value)
    return
  // Mirror the backend [PermissionAuthorize] attributes so the UI never offers a
  // command the current user cannot execute.
  if (action !== 'convert' && !canExecute.value)
    return
  if (action === 'convert' && !canCreateLead.value)
    return
  const ids = [...selectedCandidateIds.value]
  if (ids.length === 0) {
    toast.warning(t('crm.prospecting.bulkNoSelection'))
    return
  }
  if (ids.length > 200) {
    toast.warning(t('crm.prospecting.bulkLimit'))
    return
  }
  const confirmKey = bulkConfirmKeys[action]
  if (confirmKey && !window.confirm(t(confirmKey, { count: ids.length })))
    return

  bulkBusy.value = true
  try {
    let result: CrmProspectingBulkResultDto
    if (action === 'approve' || action === 'reject')
      result = await crmProspectingApi.bulkReviewCandidates({ candidateIds: ids, approve: action === 'approve', overrideDuplicate: false })
    else if (action === 'convert')
      result = await crmProspectingApi.bulkConvertCandidates({ candidateIds: ids })
    else if (action === 'shortlist')
      result = await crmProspectingApi.shortlistCandidates({ candidateIds: ids })
    else
      result = await crmProspectingApi.unshortlistCandidates({ candidateIds: ids })

    bulkResult.value = result
    const success = result.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Success).length
    const skipped = result.items.filter(item => item.outcome === CrmProspectingBulkOutcome.Skipped).length
    const failed = result.items.length - success - skipped
    const summary = t('crm.prospecting.bulkResultSummary', { success, skipped, failed })
    if (failed > 0)
      toast.warning(summary)
    else
      toast.success(summary)
    selectedCandidateIds.value = []
    await loadCandidates()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.bulkError'))
  }
  finally {
    bulkBusy.value = false
  }
}

onMounted(() => {
  void loadCampaigns()
  void loadProviderCatalog()
})
</script>

<template>
  <main class="crm-prospecting-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.prospecting.title') }}</h1>
        <p>{{ t('crm.prospecting.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCampaignDialog()">
        {{ t('crm.prospecting.createCampaign') }}
      </button>
    </header>

    <ProviderCredentialsPanel />
    <SuperSearchPreviewPanel @run-created="loadWorkspace" />
    <SchedulePanel @schedule-changed="loadWorkspace" />

    <section class="toolbar">
      <label>{{ t('crm.prospecting.campaign') }}
        <select v-model="selectedCampaignId" :disabled="loading">
          <option value="">{{ t('crm.prospecting.selectCampaign') }}</option>
          <option v-for="campaign in campaigns" :key="campaign.basicId" :value="String(campaign.basicId)">{{ campaign.name }}</option>
        </select>
      </label>
      <button type="button" :disabled="loading" @click="loadCampaigns">
        {{ t('crm.prospecting.refresh') }}
      </button>
      <button v-if="canCreate && selectedCampaign" type="button" @click="editCurrentCampaign">
        {{ t('crm.prospecting.editCampaign') }}
      </button>
      <span v-if="selectedCampaign" class="status-pill">{{ campaignStatus(selectedCampaign.status) }}</span>
    </section>

    <template v-if="selectedCampaign">
      <section class="panel">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.strategies') }}</h2><p>{{ t('crm.prospecting.strategyDescription') }}</p></div>
          <button v-if="canCreate" type="button" @click="openStrategyDialog">
            {{ t('crm.prospecting.createStrategy') }}
          </button>
        </header>
        <div v-if="strategies.length === 0" class="empty-state">
          {{ t('crm.prospecting.noStrategies') }}
        </div>
        <div v-for="strategy in strategies" :key="strategy.basicId" class="record-row">
          <div class="record-main">
            <strong>{{ strategy.name }} · v{{ strategy.version }}</strong>
            <small>{{ strategy.searchObjective }}</small>
            <small>{{ t('crm.prospecting.providerPlan') }}: {{ strategy.providerPlanJson }}</small>
          </div>
          <span class="status-pill">{{ strategyStatus(strategy.approvalStatus) }}</span>
          <div class="actions">
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.Draft" type="button" @click="submitStrategy(strategy)">
              {{ t('crm.prospecting.submitApproval') }}
            </button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.PendingApproval" type="button" @click="decideStrategy(strategy, true)">
              {{ t('crm.prospecting.approve') }}
            </button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.PendingApproval" type="button" @click="decideStrategy(strategy, false)">
              {{ t('crm.prospecting.reject') }}
            </button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.Approved" type="button" @click="activateStrategy(strategy)">
              {{ t('crm.prospecting.activate') }}
            </button>
          </div>
        </div>
      </section>

      <section class="panel">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.runs') }}</h2><p>{{ t('crm.prospecting.runDescription') }}</p></div>
          <button v-if="canExecute" class="primary-button" type="button" :disabled="!selectedCampaign.activeStrategyVersion || submitting" @click="startRun">
            {{ t('crm.prospecting.startRun') }}
          </button>
        </header>
        <div v-if="runs.length === 0" class="empty-state">
          {{ t('crm.prospecting.noRuns') }}
        </div>
        <table v-else>
          <thead><tr><th>ID</th><th>{{ t('crm.prospecting.strategyVersion') }}</th><th>{{ t('crm.prospecting.status') }}</th><th>{{ t('crm.prospecting.rawCount') }}</th><th>{{ t('crm.prospecting.candidateCount') }}</th><th>{{ t('crm.prospecting.failures') }}</th><th>{{ t('crm.prospecting.created') }}</th><th>{{ t('crm.prospecting.actions') }}</th></tr></thead>
          <tbody>
            <tr v-for="run in runs" :key="run.basicId" :class="{ selected: selectedRunId === String(run.basicId) }">
              <td>
                <button class="text-button" type="button" @click="selectRun(run)">
                  {{ run.basicId }}
                </button>
              </td>
              <td>v{{ run.strategyVersion }}</td><td>{{ runStatus(run.status) }}</td><td>{{ run.rawRecordCount }}</td><td>{{ run.candidateCount }}</td><td>{{ run.failureCount }}</td><td>{{ new Date(run.createdTime).toLocaleString() }}</td>
              <td>
                <button v-if="canExecute && [CrmProspectingRunStatus.Queued, CrmProspectingRunStatus.Running, CrmProspectingRunStatus.WaitingForExtractions].includes(run.status)" type="button" @click="pauseRun(run)">
                  {{ t('crm.prospecting.pauseRun') }}
                </button>
                <button v-if="canExecute && run.status === CrmProspectingRunStatus.Paused" type="button" @click="resumeRun(run)">
                  {{ t('crm.prospecting.resumeRun') }}
                </button>
                <button v-if="canExecute && [CrmProspectingRunStatus.Queued, CrmProspectingRunStatus.Running, CrmProspectingRunStatus.WaitingForExtractions, CrmProspectingRunStatus.Paused].includes(run.status)" type="button" @click="cancelRun(run)">
                  {{ t('crm.prospecting.cancel') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section v-if="selectedRunId" class="panel">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.candidates') }}</h2><p>{{ t('crm.prospecting.candidateDescription', { runId: selectedRunId }) }}</p></div>
          <label class="inline-filter">{{ t('crm.prospecting.candidateStatusFilter') }}
            <select v-model.number="candidateStatusFilter">
              <option value="">{{ t('crm.prospecting.allStatuses') }}</option>
              <option v-for="status in [0, 1, 2, 3, 4, 5]" :key="status" :value="status">{{ candidateStatus(status) }}</option>
            </select>
          </label>
          <label class="inline-filter">{{ t('crm.prospecting.shortlistFilter') }}
            <select v-model="candidateShortlistFilter">
              <option value="">{{ t('crm.prospecting.shortlistAll') }}</option>
              <option value="true">{{ t('crm.prospecting.shortlistOnly') }}</option>
              <option value="false">{{ t('crm.prospecting.shortlistExcluded') }}</option>
            </select>
          </label>
          <button type="button" @click="loadCandidates">
            {{ t('crm.prospecting.refresh') }}
          </button>
          <button type="button" @click="refreshRunDetail">
            {{ t('crm.prospecting.refreshRun') }}
          </button>
          <button v-if="canExport" type="button" :disabled="exporting" @click="exportCandidates">
            {{ t('crm.prospecting.exportCandidates') }}
          </button>
        </header>
        <BulkActionBar
          :selected-count="selectedCandidateIds.length"
          :busy="bulkBusy"
          :can-execute="canExecute"
          :can-create-lead="canCreateLead"
          :result="bulkResult"
          @action="runBulkAction"
          @clear="clearSelection"
          @dismiss="bulkResult = null"
        />
        <RunMonitorPanel
          v-if="runDetail"
          :run="runDetail"
          :monitor="runMonitor"
          :polling="shouldPollRun"
        />
        <CandidateTable
          :candidates="candidates"
          :selected-ids="selectedCandidateIds"
          :can-execute="canExecute"
          :can-create-lead="canCreateLead"
          :page-index="candidatePageIndex"
          :total-pages="candidateTotalPages"
          :total="candidateTotal"
          @toggle="toggleCandidate"
          @toggle-all="toggleSelectAll"
          @view="openCandidateReview"
          @review="reviewCandidate"
          @convert="convertCandidate"
          @page="gotoCandidatePage"
        />
      </section>

      <ReviewDetailPanel
        :loading="reviewDetailLoading"
        :detail="reviewDetail"
        @close="reviewDetail = null"
      />
    </template>
    <div v-else class="empty-state">
      {{ t('crm.prospecting.noCampaigns') }}
    </div>

    <div v-if="campaignDialog" class="modal-backdrop" @click.self="campaignDialog = false">
      <section class="editor" role="dialog" aria-modal="true">
        <header>
          <h2>{{ editingCampaignId ? t('crm.prospecting.editCampaign') : t('crm.prospecting.createCampaign') }}</h2><button type="button" @click="campaignDialog = false">
            ×
          </button>
        </header>
        <form @submit.prevent="saveCampaign">
          <label>{{ t('crm.prospecting.name') }} *<input v-model="campaignForm.name" maxlength="160" required></label>
          <label>{{ t('crm.prospecting.objective') }}<textarea v-model="campaignForm.objective" rows="2" maxlength="1000" /></label>
          <label>{{ t('crm.prospecting.productScope') }}<input v-model="campaignForm.productScope" maxlength="500"></label>
          <label>{{ t('crm.prospecting.targetMarkets') }}<textarea v-model="campaignForm.targetMarkets" rows="2" /></label>
          <label>{{ t('crm.prospecting.idealCustomerProfile') }}<textarea v-model="campaignForm.idealCustomerProfile" rows="3" /></label>
          <footer>
            <button type="button" @click="campaignDialog = false">
              {{ t('crm.prospecting.cancel') }}
            </button><button class="primary-button" type="submit" :disabled="submitting">
              {{ t('crm.prospecting.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>

    <div v-if="strategyDialog" class="modal-backdrop" @click.self="strategyDialog = false">
      <section class="editor" role="dialog" aria-modal="true">
        <header>
          <h2>{{ t('crm.prospecting.createStrategy') }}</h2><button type="button" @click="strategyDialog = false">
            ×
          </button>
        </header>
        <form @submit.prevent="saveStrategy">
          <label>{{ t('crm.prospecting.name') }} *<input v-model="strategyForm.name" maxlength="160" required></label>
          <label>{{ t('crm.prospecting.searchObjective') }} *<textarea v-model="strategyForm.searchObjective" rows="4" required /></label>
          <div class="provider-catalog">
            <strong>{{ t('crm.prospecting.providerCatalog') }}</strong>
            <p class="hint">
              {{ t('crm.prospecting.providerCatalogHint') }}
            </p>
            <p v-if="catalogIsFallback" class="hint catalog-warning">
              {{ t('crm.prospecting.catalogFallback') }}
            </p>
            <p v-else class="hint">
              {{ t('crm.prospecting.catalogEnabledSummary', { enabled: catalogRegisteredCount, total: catalogTotalCount }) }}
            </p>
            <div v-for="group in CRM_PROSPECTING_PROVIDER_GROUPS" :key="group.key" class="provider-group">
              <h4>{{ group.label }}</h4>
              <div class="provider-options">
                <label
                  v-for="item in catalogFor(group.key)"
                  :key="item.code"
                  class="provider-option"
                  :class="{ selected: isProviderSelected(item.code), unavailable: !catalogIsFallback && !item.isExecutable }"
                  :title="item.configKey || undefined"
                >
                  <input type="checkbox" :checked="isProviderSelected(item.code)" @change="toggleProvider(item.code, $event)">
                  <span><code>{{ item.code }}</code> · {{ item.label }}<small>{{ providerStatusLabel(item) }} · {{ providerCredentialLabel(item) }} · {{ item.note }}</small></span>
                </label>
              </div>
            </div>
          </div>
          <label>{{ t('crm.prospecting.providerPlanJson') }}<textarea v-model="strategyForm.providerPlanJson" rows="2" required @input="onProviderPlanInput" /></label>
          <label>{{ t('crm.prospecting.excludedDomainsJson') }}<textarea v-model="strategyForm.excludedDomainsJson" rows="2" /></label>
          <p class="hint">
            {{ t('crm.prospecting.providerOptInNotice') }}
          </p>
          <footer>
            <button type="button" @click="strategyDialog = false">
              {{ t('crm.prospecting.cancel') }}
            </button><button class="primary-button" type="submit" :disabled="submitting">
              {{ t('crm.prospecting.save') }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-prospecting-page {
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
}
.page-header,
.section-header,
.toolbar,
.record-row,
.editor header,
.editor footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}
.page-header h1,
.section-header h2,
.editor h2 {
  margin: 0;
}
.page-header p,
.section-header p,
.record-main small,
.hint {
  color: var(--xh-text-muted, #777);
  margin: 0.25rem 0 0;
}
.panel {
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.75rem;
  padding: 1rem;
  overflow-x: auto;
}
.record-row {
  border-top: 1px solid var(--xh-border, #ddd);
  padding: 0.75rem 0;
  flex-wrap: wrap;
}
.record-main {
  display: grid;
  gap: 0.2rem;
  min-width: 14rem;
  flex: 1;
}
.toolbar label,
.editor label {
  display: grid;
  gap: 0.35rem;
}
.toolbar select,
.editor input,
.editor textarea {
  min-width: 12rem;
  border: 1px solid var(--xh-border, #ccc);
  border-radius: 0.4rem;
  padding: 0.55rem;
  background: var(--xh-surface, white);
  color: inherit;
}
button {
  border: 1px solid var(--xh-border, #ccc);
  border-radius: 0.4rem;
  padding: 0.45rem 0.7rem;
}
button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.primary-button {
  background: var(--xh-primary, #2563eb);
  color: white;
  border-color: transparent;
}
.text-button {
  border: 0;
  color: var(--xh-primary, #2563eb);
  background: transparent;
}
.status-pill {
  border-radius: 999px;
  padding: 0.2rem 0.6rem;
  background: var(--xh-muted-surface, #eee);
  white-space: nowrap;
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 0.75rem;
  font-size: 0.9rem;
}
th,
td {
  padding: 0.65rem 0.5rem;
  border-bottom: 1px solid var(--xh-border, #ddd);
  text-align: left;
  vertical-align: top;
}
td small {
  display: block;
  color: var(--xh-text-muted, #777);
}
tr.selected {
  background: var(--xh-muted-surface, #f5f5f5);
}
.empty-state {
  padding: 1.5rem;
  text-align: center;
  color: var(--xh-text-muted, #777);
}
.modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: #0008;
}
.editor {
  width: min(42rem, 100%);
  max-height: 90vh;
  overflow-y: auto;
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
  border-radius: 0.75rem;
  background: var(--xh-surface, white);
  color: inherit;
}
.editor form {
  display: grid;
  gap: 0.75rem;
}
.editor footer {
  justify-content: flex-end;
  margin-top: 0.5rem;
}
@media (max-width: 760px) {
  .crm-prospecting-page {
    padding: 0.75rem;
  }
  .page-header,
  .section-header,
  .toolbar {
    align-items: stretch;
    flex-direction: column;
  }
}
.provider-catalog {
  display: grid;
  gap: 0.5rem;
  max-height: 18rem;
  overflow-y: auto;
  padding: 0.75rem;
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.5rem;
}
.provider-group h4 {
  margin: 0.5rem 0 0.25rem;
  font-size: 0.85rem;
}
.provider-options {
  display: grid;
  gap: 0.35rem;
  grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
}
.provider-option {
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
  padding: 0.45rem 0.5rem;
  border: 1px solid var(--xh-border, #ddd);
  border-radius: 0.4rem;
  cursor: pointer;
}
.provider-option.selected {
  border-color: var(--xh-primary, #2563eb);
  background: var(--xh-muted-surface, #f5f5f5);
}
.provider-option input {
  margin-top: 0.15rem;
  flex: none;
}
.provider-option code {
  font-size: 0.8rem;
}
.provider-option small {
  display: block;
  margin-top: 0.15rem;
  color: var(--xh-text-muted, #777);
  line-height: 1.4;
}
.provider-option.unavailable {
  border-style: dashed;
  opacity: 0.72;
}
.catalog-warning {
  color: var(--xh-warning, #b45309);
}
.inline-filter {
  display: grid;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.inline-filter select {
  border: 1px solid var(--xh-border, #ccc);
  border-radius: 0.4rem;
  padding: 0.4rem;
  background: var(--xh-surface, white);
  color: inherit;
}
</style>
