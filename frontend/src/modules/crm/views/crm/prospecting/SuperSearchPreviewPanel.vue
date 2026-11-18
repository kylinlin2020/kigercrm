<script setup lang="ts">
import type {
  ApiId,
  CrmProspectingCampaignDto,
  CrmProspectingSuperSearchMissionDto,
  CrmProspectingSuperSearchPreviewDto,
  CrmProspectingSuperSearchPreviewInputDto,
} from '@/api'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  crmProspectingApi,
  CrmProspectingSuperSearchDepth,
  CrmProspectingSuperSearchMissionStatus,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingSuperSearchPreviewPanel' })

const emit = defineEmits<{ runCreated: [runId: ApiId] }>()

const { t } = useI18n()
const { hasPermission } = usePermission()
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))

const campaigns = ref<CrmProspectingCampaignDto[]>([])
const campaignId = ref('')
const missionName = ref('')
const requestKey = ref('')
const missions = ref<CrmProspectingSuperSearchMissionDto[]>([])

const products = ref('')
const markets = ref('')
const customerTypes = ref('')
const industries = ref('')
const providerCodes = ref('')
const excludedTerms = ref('')
const excludedDomains = ref('')
const depth = ref(CrmProspectingSuperSearchDepth.Balanced)
const rounds = ref('')

const preview = ref<CrmProspectingSuperSearchPreviewDto | null>(null)
const submitting = ref(false)
const savingMission = ref(false)

const maxRounds = computed(() => [4, 8, 16][Number(depth.value)] ?? 4)

function splitList(value: string): string[] {
  return value.split(/[\n,，;；]+/).map(item => item.trim()).filter(Boolean)
}

function buildPlanInput(): CrmProspectingSuperSearchPreviewInputDto {
  return {
    products: splitList(products.value),
    markets: splitList(markets.value),
    customerTypes: splitList(customerTypes.value),
    industries: splitList(industries.value),
    providerCodes: splitList(providerCodes.value),
    excludedTerms: splitList(excludedTerms.value),
    excludedDomains: splitList(excludedDomains.value),
    depth: Number(depth.value) as CrmProspectingSuperSearchDepth,
    requestedRounds: rounds.value === '' ? null : Number(rounds.value),
  }
}

async function loadCampaigns() {
  try {
    campaigns.value = await crmProspectingApi.campaigns()
    if (!campaigns.value.some(item => String(item.basicId) === campaignId.value)) {
      campaignId.value = campaigns.value[0] ? String(campaigns.value[0].basicId) : ''
    }
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
}

async function loadMissions() {
  if (!campaignId.value) {
    missions.value = []
    return
  }
  try {
    missions.value = await crmProspectingApi.superSearchMissions(campaignId.value)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.loadError'))
  }
}

onMounted(async () => {
  await loadCampaigns()
  await loadMissions()
})

watch(campaignId, () => {
  void loadMissions()
})

async function submitPreview() {
  if (!canExecute.value || submitting.value) {
    return
  }
  submitting.value = true
  try {
    preview.value = await crmProspectingApi.previewSuperSearch(buildPlanInput())
  }
  catch (error) {
    // A rejected preview must never leave a stale plan on screen.
    preview.value = null
    toast.danger((error as Error)?.message || t('crm.prospecting.superSearchError'))
  }
  finally {
    submitting.value = false
  }
}

async function saveMission() {
  if (!canExecute.value || savingMission.value || !campaignId.value) {
    return
  }
  savingMission.value = true
  try {
    await crmProspectingApi.saveSuperSearchMission({
      campaignId: campaignId.value,
      name: missionName.value,
      requestKey: requestKey.value,
      plan: buildPlanInput(),
    })
    toast.success(t('crm.prospecting.superSearchSaved'))
    await loadMissions()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.superSearchError'))
  }
  finally {
    savingMission.value = false
  }
}

async function executeMission(mission: CrmProspectingSuperSearchMissionDto) {
  if (!canExecute.value || mission.runId) {
    return
  }
  try {
    const run = await crmProspectingApi.createRunFromSuperSearchMission(mission.basicId)
    toast.success(t('crm.prospecting.superSearchMissionRunCreated', { id: run.basicId }))
    emit('runCreated', run.basicId)
    await loadMissions()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.superSearchError'))
  }
}

function fingerprintShort(value: string) {
  return value.length > 12 ? `${value.slice(0, 12)}…` : value
}
</script>

<template>
  <section class="super-search">
    <header class="section-header">
      <div>
        <h2>{{ t('crm.prospecting.superSearchTitle') }}</h2>
        <p>{{ t('crm.prospecting.superSearchDescription') }}</p>
      </div>
      <button
        v-if="canExecute"
        class="primary-button"
        type="button"
        :disabled="submitting"
        @click="submitPreview"
      >
        {{ submitting ? t('crm.prospecting.superSearchSubmitting') : t('crm.prospecting.superSearchPreviewButton') }}
      </button>
    </header>

    <div class="search-form">
      <label>{{ t('crm.prospecting.campaign') }}
        <select v-model="campaignId">
          <option value="">{{ t('crm.prospecting.selectCampaign') }}</option>
          <option v-for="campaign in campaigns" :key="campaign.basicId" :value="String(campaign.basicId)">
            {{ campaign.name }}
          </option>
        </select>
      </label>
      <label>{{ t('crm.prospecting.superSearchMissionName') }}
        <input v-model="missionName" type="text" maxlength="160">
      </label>
      <label>{{ t('crm.prospecting.superSearchMissionRequestKey') }}
        <input v-model="requestKey" type="text" maxlength="100">
      </label>
      <label>{{ t('crm.prospecting.superSearchProducts') }}
        <textarea v-model="products" rows="2" placeholder="industrial pump, valve" />
      </label>
      <label>{{ t('crm.prospecting.superSearchMarkets') }}
        <textarea v-model="markets" rows="2" placeholder="DE, BR" />
      </label>
      <label>{{ t('crm.prospecting.superSearchCustomerTypes') }}
        <textarea v-model="customerTypes" rows="2" placeholder="distributor, importer" />
      </label>
      <label>{{ t('crm.prospecting.superSearchIndustries') }}
        <textarea v-model="industries" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchProviders') }}
        <textarea v-model="providerCodes" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchExcludedTerms') }}
        <textarea v-model="excludedTerms" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchExcludedDomains') }}
        <textarea v-model="excludedDomains" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchDepth') }}
        <select v-model.number="depth">
          <option :value="CrmProspectingSuperSearchDepth.Balanced">Balanced</option>
          <option :value="CrmProspectingSuperSearchDepth.Deep">Deep</option>
          <option :value="CrmProspectingSuperSearchDepth.Extreme">Extreme</option>
        </select>
      </label>
      <label>{{ t('crm.prospecting.superSearchRounds') }}
        <input v-model="rounds" type="number" min="1" :max="maxRounds" :placeholder="`1–${maxRounds}`">
      </label>
      <button
        v-if="canExecute"
        class="primary-button"
        type="button"
        :disabled="savingMission || !campaignId"
        @click="saveMission"
      >
        {{ t('crm.prospecting.superSearchSaveMission') }}
      </button>
    </div>

    <p class="read-only-notice">
      {{ t('crm.prospecting.superSearchReadOnlyNotice') }}
    </p>

    <div role="status" aria-live="polite">
      <template v-if="preview">
        <div class="plan-summary">
          <span>{{ t('crm.prospecting.superSearchSummary', {
            rounds: preview.plannedRounds,
            maximum: preview.maximumRounds,
            cells: preview.totalQueryCells,
            providers: preview.providerCount,
          }) }}</span>
          <code :title="preview.scopeFingerprint">{{ fingerprintShort(preview.scopeFingerprint) }}</code>
        </div>
        <p v-if="preview.rounds.length === 0" class="empty-state">
          {{ t('crm.prospecting.superSearchNoRounds') }}
        </p>
        <details v-for="round in preview.rounds" :key="round.fingerprint" class="plan-round">
          <summary>
            {{ t('crm.prospecting.superSearchRound', { number: round.roundNumber }) }}
            · {{ round.themeLabel }} · {{ round.market }} / {{ round.customerType }}
          </summary>
          <div class="round-meta">
            <span>{{ t('crm.prospecting.superSearchExcludedTerms') }}: {{ round.excludedTerms.join(', ') || '—' }}</span>
            <span>{{ t('crm.prospecting.superSearchExcludedDomains') }}: {{ round.excludedDomains.join(', ') || '—' }}</span>
            <code :title="round.fingerprint">{{ fingerprintShort(round.fingerprint) }}</code>
          </div>
          <table class="cell-table">
            <thead>
              <tr>
                <th>{{ t('crm.prospecting.provider') }}</th>
                <th>{{ t('crm.prospecting.superSearchTheme') }}</th>
                <th>{{ t('crm.prospecting.superSearchQueryCells') }}</th>
                <th>{{ t('crm.prospecting.superSearchFingerprint') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="cell in round.queryCells" :key="cell.fingerprint">
                <td><code>{{ cell.providerCode }}</code></td>
                <td><code>{{ cell.themeCode }}</code></td>
                <td class="query-cell">
                  {{ cell.queryText }}
                </td>
                <td><code :title="cell.fingerprint">{{ fingerprintShort(cell.fingerprint) }}</code></td>
              </tr>
            </tbody>
          </table>
        </details>
      </template>
    </div>

    <div class="mission-list">
      <h3>{{ t('crm.prospecting.superSearchMissionList') }}</h3>
      <p v-if="missions.length === 0" class="empty-state">
        {{ t('crm.prospecting.superSearchMissionEmpty') }}
      </p>
      <div v-for="mission in missions" :key="mission.basicId" class="mission-row">
        <div class="mission-main">
          <strong>{{ mission.name }}</strong>
          <small>{{ mission.requestKey }} · {{ mission.plannedRounds }} {{ t('crm.prospecting.superSearchRounds') }} · {{ fingerprintShort(mission.scopeFingerprint) }}</small>
        </div>
        <span class="status-pill">
          {{ mission.status === CrmProspectingSuperSearchMissionStatus.Executed
            ? t('crm.prospecting.superSearchMissionExecuted')
            : t('crm.prospecting.superSearchMissionPlanned') }}
        </span>
        <button
          v-if="canExecute && !mission.runId"
          type="button"
          @click="executeMission(mission)"
        >
          {{ t('crm.prospecting.superSearchMissionExecute') }}
        </button>
        <code v-if="mission.runId">#{{ mission.runId }}</code>
      </div>
    </div>
  </section>
</template>

<style scoped>
.super-search {
  display: grid;
  gap: 0.75rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.search-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.search-form label {
  display: grid;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.search-form textarea,
.search-form input,
.search-form select {
  width: 100%;
  font: inherit;
}
.read-only-notice {
  margin: 0;
  color: var(--xh-fg-muted, #777);
  font-size: 0.8rem;
}
.plan-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.plan-round {
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  padding: 0.5rem 0;
}
.plan-round summary {
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
}
.round-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin: 0.4rem 0;
  color: var(--xh-fg-muted, #777);
  font-size: 0.8rem;
}
.cell-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}
.cell-table th,
.cell-table td {
  padding: 0.4rem 0.5rem;
  border-bottom: 1px solid var(--xh-border-subtle, #ddd);
  text-align: left;
  vertical-align: top;
}
.query-cell {
  max-width: 24rem;
  overflow-wrap: anywhere;
}
.mission-list {
  display: grid;
  gap: 0.4rem;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  padding-top: 0.5rem;
}
.mission-list h3 {
  margin: 0;
  font-size: 0.95rem;
}
.mission-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-bottom: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.mission-main {
  display: grid;
  gap: 0.2rem;
  min-width: 0;
}
.mission-main small {
  color: var(--xh-fg-muted, #777);
}
.empty-state {
  margin: 0;
  color: var(--xh-fg-muted, #777);
}
</style>
