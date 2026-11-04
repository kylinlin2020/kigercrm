<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  CrmProspectingCandidateStatus,
  CrmProspectingRunStatus,
  CrmProspectingStrategyApprovalStatus,
  crmProspectingApi,
} from '@/api'
import type {
  CrmProspectingCampaignDto,
  CrmProspectingCandidateDto,
  CrmProspectingCandidateReviewDetailDto,
  CrmProspectingRunDto,
  CrmProspectingStrategyDto,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingPage' })

const { t } = useI18n()
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
const strategyForm = ref({ name: 'Default search strategy', searchObjective: '', providerPlanJson: '["fake"]', excludedDomainsJson: '[]' })

const selectedCampaign = computed(() => campaigns.value.find(item => String(item.basicId) === selectedCampaignId.value) ?? null)
const canCreate = computed(() => hasPermission('crm_prospecting.create'))
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))
const canCreateLead = computed(() => hasPermission('crm:create'))

function campaignStatus(value: number) {
  return [t('crm.prospecting.draft'), t('crm.prospecting.active'), t('crm.prospecting.paused'), t('crm.prospecting.completed'), t('crm.prospecting.archived')][value] ?? String(value)
}
function strategyStatus(value: number) {
  return [t('crm.prospecting.strategyDraft'), t('crm.prospecting.pendingApproval'), t('crm.prospecting.approved'), t('crm.prospecting.rejected'), t('crm.prospecting.revoked')][value] ?? String(value)
}
function runStatus(value: number) {
  return [t('crm.prospecting.queued'), t('crm.prospecting.running'), t('crm.prospecting.succeeded'), t('crm.prospecting.partiallySucceeded'), t('crm.prospecting.failed'), t('crm.prospecting.cancelRequested'), t('crm.prospecting.cancelled')][value] ?? String(value)
}
function candidateStatus(value: number) {
  return [t('crm.prospecting.pendingReview'), t('crm.prospecting.duplicate'), t('crm.prospecting.approved'), t('crm.prospecting.rejected'), t('crm.prospecting.convertedStatus')][value] ?? String(value)
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
    selectedRunId.value = ''
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
    return
  }
  try {
    candidates.value = await crmProspectingApi.candidates(selectedRunId.value)
  }
  catch (error) {
    candidates.value = []
    toast.danger((error as Error)?.message || t('crm.prospecting.candidateLoadError'))
  }
}

watch(selectedCampaignId, () => { void loadWorkspace() })
watch(selectedRunId, () => { void loadCandidates() })

function openCampaignDialog() {
  campaignForm.value = { name: '', objective: '', productScope: '', targetMarkets: '', idealCustomerProfile: '' }
  campaignDialog.value = true
}
async function saveCampaign() {
  if (!campaignForm.value.name.trim()) {
    toast.warning(t('crm.prospecting.nameRequired'))
    return
  }
  submitting.value = true
  try {
    const created = await crmProspectingApi.createCampaign({
      name: campaignForm.value.name.trim(),
      objective: campaignForm.value.objective || null,
      productScope: campaignForm.value.productScope || null,
      targetMarkets: campaignForm.value.targetMarkets || null,
      idealCustomerProfile: campaignForm.value.idealCustomerProfile || null,
    })
    campaignDialog.value = false
    await loadCampaigns()
    selectedCampaignId.value = String(created.basicId)
    toast.success(t('crm.prospecting.campaignCreated'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.saveError'))
  }
  finally {
    submitting.value = false
  }
}
function openStrategyDialog() {
  strategyForm.value = { name: 'Default search strategy', searchObjective: selectedCampaign.value?.objective || '', providerPlanJson: '["fake"]', excludedDomainsJson: '[]' }
  strategyDialog.value = true
}
async function saveStrategy() {
  if (!selectedCampaignId.value || !strategyForm.value.name.trim() || !strategyForm.value.searchObjective.trim()) {
    toast.warning(t('crm.prospecting.strategyRequired'))
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
  if (!window.confirm(approve ? t('crm.prospecting.confirmApproveStrategy') : t('crm.prospecting.confirmRejectStrategy'))) return
  try {
    await crmProspectingApi.decideStrategy({ strategyId: strategy.basicId, approve })
    await loadWorkspace()
    toast.success(t('crm.prospecting.strategyDecisionSaved'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function activateStrategy(strategy: CrmProspectingStrategyDto) {
  if (!window.confirm(t('crm.prospecting.confirmActivate'))) return
  try {
    await crmProspectingApi.activateStrategy(strategy.basicId)
    await loadWorkspace()
    await loadCampaigns()
    toast.success(t('crm.prospecting.strategyActivated'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}
async function startRun() {
  if (!selectedCampaignId.value || !selectedCampaign.value?.activeStrategyVersion) return
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
  if (!window.confirm(t('crm.prospecting.confirmCancelRun'))) return
  try {
    await crmProspectingApi.cancelRun(run.basicId)
    await loadWorkspace()
    toast.success(t('crm.prospecting.cancelRequested'))
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

function scorePriority(value: number) {
  return [t('crm.prospecting.priorityHold'), 'C', 'B', 'A'][value] ?? String(value)
}

function scoreGates(value: string) {
  try {
    const parsed = JSON.parse(value)
    return Array.isArray(parsed) ? parsed.join(', ') : value
  }
  catch {
    return value
  }
}

async function reviewCandidate(candidate: CrmProspectingCandidateDto, approve: boolean) {
  if (approve && candidate.status === CrmProspectingCandidateStatus.Duplicate && !window.confirm(t('crm.prospecting.confirmDuplicateOverride'))) return
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
  if (!canCreateLead.value || !window.confirm(t('crm.prospecting.confirmConvert', { name: candidate.companyName }))) return
  try {
    await crmProspectingApi.convertCandidate(candidate.basicId)
    await loadCandidates()
    toast.success(t('crm.prospecting.converted'))
  }
  catch (error) { toast.danger((error as Error)?.message || t('crm.prospecting.actionError')) }
}

onMounted(loadCampaigns)
</script>

<template>
  <main class="crm-prospecting-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.prospecting.title') }}</h1>
        <p>{{ t('crm.prospecting.description') }}</p>
      </div>
      <button v-if="canCreate" class="primary-button" type="button" @click="openCampaignDialog">
        {{ t('crm.prospecting.createCampaign') }}
      </button>
    </header>

    <section class="toolbar">
      <label>{{ t('crm.prospecting.campaign') }}
        <select v-model="selectedCampaignId" :disabled="loading">
          <option value="">{{ t('crm.prospecting.selectCampaign') }}</option>
          <option v-for="campaign in campaigns" :key="campaign.basicId" :value="String(campaign.basicId)">{{ campaign.name }}</option>
        </select>
      </label>
      <button type="button" :disabled="loading" @click="loadCampaigns">{{ t('crm.prospecting.refresh') }}</button>
      <span v-if="selectedCampaign" class="status-pill">{{ campaignStatus(selectedCampaign.status) }}</span>
    </section>

    <template v-if="selectedCampaign">
      <section class="panel">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.strategies') }}</h2><p>{{ t('crm.prospecting.strategyDescription') }}</p></div>
          <button v-if="canCreate" type="button" @click="openStrategyDialog">{{ t('crm.prospecting.createStrategy') }}</button>
        </header>
        <div v-if="strategies.length === 0" class="empty-state">{{ t('crm.prospecting.noStrategies') }}</div>
        <div v-for="strategy in strategies" :key="strategy.basicId" class="record-row">
          <div class="record-main">
            <strong>{{ strategy.name }} · v{{ strategy.version }}</strong>
            <small>{{ strategy.searchObjective }}</small>
            <small>{{ t('crm.prospecting.providerPlan') }}: {{ strategy.providerPlanJson }}</small>
          </div>
          <span class="status-pill">{{ strategyStatus(strategy.approvalStatus) }}</span>
          <div class="actions">
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.Draft" type="button" @click="submitStrategy(strategy)">{{ t('crm.prospecting.submitApproval') }}</button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.PendingApproval" type="button" @click="decideStrategy(strategy, true)">{{ t('crm.prospecting.approve') }}</button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.PendingApproval" type="button" @click="decideStrategy(strategy, false)">{{ t('crm.prospecting.reject') }}</button>
            <button v-if="canExecute && strategy.approvalStatus === CrmProspectingStrategyApprovalStatus.Approved" type="button" @click="activateStrategy(strategy)">{{ t('crm.prospecting.activate') }}</button>
          </div>
        </div>
      </section>

      <section class="panel">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.runs') }}</h2><p>{{ t('crm.prospecting.runDescription') }}</p></div>
          <button v-if="canExecute" class="primary-button" type="button" :disabled="!selectedCampaign.activeStrategyVersion || submitting" @click="startRun">{{ t('crm.prospecting.startRun') }}</button>
        </header>
        <div v-if="runs.length === 0" class="empty-state">{{ t('crm.prospecting.noRuns') }}</div>
        <table v-else>
          <thead><tr><th>ID</th><th>{{ t('crm.prospecting.strategyVersion') }}</th><th>{{ t('crm.prospecting.status') }}</th><th>{{ t('crm.prospecting.rawCount') }}</th><th>{{ t('crm.prospecting.candidateCount') }}</th><th>{{ t('crm.prospecting.failures') }}</th><th>{{ t('crm.prospecting.created') }}</th><th>{{ t('crm.prospecting.actions') }}</th></tr></thead>
          <tbody>
            <tr v-for="run in runs" :key="run.basicId" :class="{ selected: selectedRunId === String(run.basicId) }">
              <td><button class="text-button" type="button" @click="selectedRunId = String(run.basicId)">{{ run.basicId }}</button></td>
              <td>v{{ run.strategyVersion }}</td><td>{{ runStatus(run.status) }}</td><td>{{ run.rawRecordCount }}</td><td>{{ run.candidateCount }}</td><td>{{ run.failureCount }}</td><td>{{ new Date(run.createdTime).toLocaleString() }}</td>
              <td><button v-if="canExecute && [CrmProspectingRunStatus.Queued, CrmProspectingRunStatus.Running].includes(run.status)" type="button" @click="cancelRun(run)">{{ t('crm.prospecting.cancel') }}</button></td>
            </tr>
          </tbody>
        </table>
      </section>

      <section v-if="selectedRunId" class="panel">
        <header class="section-header"><div><h2>{{ t('crm.prospecting.candidates') }}</h2><p>{{ t('crm.prospecting.candidateDescription', { runId: selectedRunId }) }}</p></div><button type="button" @click="loadCandidates">{{ t('crm.prospecting.refresh') }}</button></header>
        <div v-if="candidates.length === 0" class="empty-state">{{ t('crm.prospecting.noCandidates') }}</div>
        <table v-else>
          <thead><tr><th>{{ t('crm.prospecting.company') }}</th><th>{{ t('crm.prospecting.contact') }}</th><th>{{ t('crm.prospecting.email') }}</th><th>{{ t('crm.prospecting.website') }}</th><th>{{ t('crm.prospecting.source') }}</th><th>{{ t('crm.prospecting.status') }}</th><th>{{ t('crm.prospecting.duplicateReason') }}</th><th>{{ t('crm.prospecting.actions') }}</th></tr></thead>
          <tbody>
            <tr v-for="candidate in candidates" :key="candidate.basicId">
              <td><strong>{{ candidate.companyName }}</strong><small>{{ candidate.title || '' }}</small></td>
              <td>{{ candidate.contactName || '—' }}</td><td>{{ candidate.businessEmail || '—' }}</td>
              <td><a v-if="candidate.website" :href="candidate.website" target="_blank" rel="noopener noreferrer">{{ candidate.website }}</a><span v-else>—</span></td>
              <td>{{ candidate.source || candidate.providerCode }}</td><td>{{ candidateStatus(candidate.status) }}</td><td>{{ candidate.duplicateReason || '—' }}</td>
              <td class="actions">
                <button type="button" @click="openCandidateReview(candidate)">{{ t('crm.prospecting.viewEvidence') }}</button>
                <template v-if="canExecute && [CrmProspectingCandidateStatus.PendingReview, CrmProspectingCandidateStatus.Duplicate].includes(candidate.status)">
                  <button type="button" @click="reviewCandidate(candidate, true)">{{ t('crm.prospecting.approve') }}</button>
                  <button type="button" @click="reviewCandidate(candidate, false)">{{ t('crm.prospecting.reject') }}</button>
                </template>
                <button v-if="canCreateLead && candidate.status === CrmProspectingCandidateStatus.Approved" type="button" @click="convertCandidate(candidate)">{{ t('crm.prospecting.convertToLead') }}</button>
              </td>
            </tr>
          </tbody>
        </table>

      </section>

      <section v-if="reviewDetailLoading || reviewDetail" class="panel review-detail">
        <header class="section-header">
          <div><h2>{{ t('crm.prospecting.reviewDetail') }}</h2><p>{{ reviewDetail?.candidate.companyName || '' }}</p></div>
          <button type="button" @click="reviewDetail = null">{{ t('crm.prospecting.closeDetail') }}</button>
        </header>
        <div v-if="reviewDetailLoading" class="empty-state">{{ t('crm.prospecting.loadingDetails') }}</div>
        <template v-else-if="reviewDetail">
          <h3>{{ t('crm.prospecting.scoreHistory') }}</h3>
          <div v-if="reviewDetail.scoreHistory.length === 0" class="empty-state">{{ t('crm.prospecting.noScore') }}</div>
          <table v-else>
            <thead><tr><th>{{ t('crm.prospecting.totalScore') }}</th><th>{{ t('crm.prospecting.priority') }}</th><th>{{ t('crm.prospecting.scoreVersion') }}</th><th>{{ t('crm.prospecting.scoreBreakdown') }}</th><th>{{ t('crm.prospecting.reviewGates') }}</th><th>{{ t('crm.prospecting.evaluatedAt') }}</th></tr></thead>
            <tbody><tr v-for="score in reviewDetail.scoreHistory" :key="score.basicId">
              <td><strong>{{ score.total }}/100</strong></td><td>{{ scorePriority(score.priority) }}</td><td>{{ score.scorecardVersion }}</td>
              <td>{{ score.identityPoints }}/15 · {{ score.procurementPoints }}/30 · {{ score.productFitPoints }}/20 · {{ score.marketFitPoints }}/10 · {{ score.recencyPoints }}/10 · {{ score.evidencePoints }}/10 · {{ score.contactPoints }}/5</td>
              <td>{{ scoreGates(score.reviewGatesJson) || '—' }}</td><td>{{ new Date(score.evaluatedAtUtc).toLocaleString() }}</td>
            </tr></tbody>
          </table>
          <h3>{{ t('crm.prospecting.evidence') }}</h3>
          <div v-if="reviewDetail.evidence.length === 0" class="empty-state">{{ t('crm.prospecting.noEvidence') }}</div>
          <table v-else>
            <thead><tr><th>{{ t('crm.prospecting.field') }}</th><th>{{ t('crm.prospecting.provider') }}</th><th>{{ t('crm.prospecting.verification') }}</th><th>{{ t('crm.prospecting.evidenceType') }}</th><th>{{ t('crm.prospecting.sourceUrl') }}</th><th>{{ t('crm.prospecting.retrievedAt') }}</th></tr></thead>
            <tbody><tr v-for="item in reviewDetail.evidence" :key="item.basicId">
              <td><strong>{{ item.fieldName }}</strong><small>{{ item.evidenceSummary || item.fieldValueHash || '' }}</small></td><td>{{ item.providerCode }}<small>{{ item.providerRecordId || '' }}</small></td><td>{{ item.verificationStatus }}</td><td>{{ item.evidenceType }}</td>
              <td><a :href="item.sourceUrl" target="_blank" rel="noopener noreferrer">{{ item.sourceUrl }}</a></td><td>{{ new Date(item.retrievedAtUtc).toLocaleString() }}</td>
            </tr></tbody>
          </table>
        </template>
      </section>
    </template>
    <div v-else class="empty-state">{{ t('crm.prospecting.noCampaigns') }}</div>

    <div v-if="campaignDialog" class="modal-backdrop" @click.self="campaignDialog = false">
      <section class="editor" role="dialog" aria-modal="true">
        <header><h2>{{ t('crm.prospecting.createCampaign') }}</h2><button type="button" @click="campaignDialog = false">×</button></header>
        <form @submit.prevent="saveCampaign">
          <label>{{ t('crm.prospecting.name') }} *<input v-model="campaignForm.name" maxlength="160" required></label>
          <label>{{ t('crm.prospecting.objective') }}<textarea v-model="campaignForm.objective" rows="2" maxlength="1000" /></label>
          <label>{{ t('crm.prospecting.productScope') }}<input v-model="campaignForm.productScope" maxlength="500"></label>
          <label>{{ t('crm.prospecting.targetMarkets') }}<textarea v-model="campaignForm.targetMarkets" rows="2" /></label>
          <label>{{ t('crm.prospecting.idealCustomerProfile') }}<textarea v-model="campaignForm.idealCustomerProfile" rows="3" /></label>
          <footer><button type="button" @click="campaignDialog = false">{{ t('crm.prospecting.cancel') }}</button><button class="primary-button" type="submit" :disabled="submitting">{{ t('crm.prospecting.save') }}</button></footer>
        </form>
      </section>
    </div>

    <div v-if="strategyDialog" class="modal-backdrop" @click.self="strategyDialog = false">
      <section class="editor" role="dialog" aria-modal="true">
        <header><h2>{{ t('crm.prospecting.createStrategy') }}</h2><button type="button" @click="strategyDialog = false">×</button></header>
        <form @submit.prevent="saveStrategy">
          <label>{{ t('crm.prospecting.name') }} *<input v-model="strategyForm.name" maxlength="160" required></label>
          <label>{{ t('crm.prospecting.searchObjective') }} *<textarea v-model="strategyForm.searchObjective" rows="4" required /></label>
          <label>{{ t('crm.prospecting.providerPlanJson') }}<textarea v-model="strategyForm.providerPlanJson" rows="2" required /></label>
          <label>{{ t('crm.prospecting.excludedDomainsJson') }}<textarea v-model="strategyForm.excludedDomainsJson" rows="2" /></label>
          <p class="hint">{{ t('crm.prospecting.fakeProviderNotice') }}</p>
          <footer><button type="button" @click="strategyDialog = false">{{ t('crm.prospecting.cancel') }}</button><button class="primary-button" type="submit" :disabled="submitting">{{ t('crm.prospecting.save') }}</button></footer>
        </form>
      </section>
    </div>
  </main>
</template>

<style scoped>
.crm-prospecting-page { display: grid; gap: 1rem; padding: 1.25rem; }
.page-header, .section-header, .toolbar, .record-row, .editor header, .editor footer { display: flex; align-items: center; justify-content: space-between; gap: .75rem; }
.page-header h1, .section-header h2, .editor h2 { margin: 0; }
.page-header p, .section-header p, .record-main small, .hint { color: var(--xh-text-muted, #777); margin: .25rem 0 0; }
.review-detail { display: grid; gap: .75rem; }
.review-detail h3 { margin: .5rem 0 0; }
.review-detail td small { display: block; max-width: 22rem; overflow-wrap: anywhere; color: var(--xh-text-muted, #777); }
.panel { border: 1px solid var(--xh-border, #ddd); border-radius: .75rem; padding: 1rem; overflow-x: auto; }
.record-row { border-top: 1px solid var(--xh-border, #ddd); padding: .75rem 0; flex-wrap: wrap; }
.record-main { display: grid; gap: .2rem; min-width: 14rem; flex: 1; }
.toolbar label, .editor label { display: grid; gap: .35rem; }
.toolbar select, .editor input, .editor textarea { min-width: 12rem; border: 1px solid var(--xh-border, #ccc); border-radius: .4rem; padding: .55rem; background: var(--xh-surface, white); color: inherit; }
button { border: 1px solid var(--xh-border, #ccc); border-radius: .4rem; padding: .45rem .7rem; }
button:disabled { opacity: .5; cursor: not-allowed; }
.primary-button { background: var(--xh-primary, #2563eb); color: white; border-color: transparent; }
.text-button { border: 0; color: var(--xh-primary, #2563eb); background: transparent; }
.status-pill { border-radius: 999px; padding: .2rem .6rem; background: var(--xh-muted-surface, #eee); white-space: nowrap; }
.actions { display: flex; flex-wrap: wrap; gap: .35rem; }
table { width: 100%; border-collapse: collapse; margin-top: .75rem; font-size: .9rem; }
th, td { padding: .65rem .5rem; border-bottom: 1px solid var(--xh-border, #ddd); text-align: left; vertical-align: top; }
td small { display: block; color: var(--xh-text-muted, #777); }
tr.selected { background: var(--xh-muted-surface, #f5f5f5); }
.empty-state { padding: 1.5rem; text-align: center; color: var(--xh-text-muted, #777); }
.modal-backdrop { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 1rem; background: #0008; }
.editor { width: min(42rem, 100%); max-height: 90vh; overflow-y: auto; display: grid; gap: 1rem; padding: 1.25rem; border-radius: .75rem; background: var(--xh-surface, white); color: inherit; }
.editor form { display: grid; gap: .75rem; }
.editor footer { justify-content: flex-end; margin-top: .5rem; }
@media (max-width: 760px) { .crm-prospecting-page { padding: .75rem; } .page-header, .section-header, .toolbar { align-items: stretch; flex-direction: column; } }
</style>
