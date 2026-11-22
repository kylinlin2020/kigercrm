<script setup lang="ts">
import type {
  ApiId,
  CrmProspectingCampaignDto,
  CrmProspectingSuperSearchMissionDto,
  CrmProspectingSuperSearchObjectiveParseResultDto,
  CrmProspectingSuperSearchPreviewDto,
  CrmProspectingSuperSearchPreviewInputDto,
} from '@/api'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  crmProspectingApi,
  CrmProspectingSuperSearchDepth,
  CrmProspectingSuperSearchMissionStatus,
} from '@/api'
import { XInput, XSelect } from '~/components'
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
const parseResult = ref<CrmProspectingSuperSearchObjectiveParseResultDto | null>(null)
const objective = ref('')
const parsing = ref(false)
const submitting = ref(false)
const savingMission = ref(false)

const maxRounds = computed(() => [4, 8, 16][Number(depth.value)] ?? 4)

const campaignOptions = computed(() => [
  { label: t('crm.prospecting.selectCampaign'), value: '' },
  ...campaigns.value.map(campaign => ({ label: campaign.name, value: String(campaign.basicId) })),
])

const depthOptions = computed(() => [
  { label: 'Balanced', value: CrmProspectingSuperSearchDepth.Balanced },
  { label: 'Deep', value: CrmProspectingSuperSearchDepth.Deep },
  { label: 'Extreme', value: CrmProspectingSuperSearchDepth.Extreme },
])

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

async function parseObjective() {
  if (!canExecute.value || parsing.value) {
    return
  }
  parsing.value = true
  try {
    const result = await crmProspectingApi.parseSuperSearchObjective({ objective: objective.value })
    // Parsed values only fill the form for human review: no auto-save, no auto-execution.
    products.value = result.input.products.join(', ')
    markets.value = result.input.markets.join(', ')
    customerTypes.value = result.input.customerTypes.join(', ')
    industries.value = result.input.industries.join(', ')
    providerCodes.value = result.input.providerCodes.join(', ')
    excludedTerms.value = result.input.excludedTerms.join(', ')
    excludedDomains.value = result.input.excludedDomains.join(', ')
    depth.value = result.input.depth as CrmProspectingSuperSearchDepth
    rounds.value = result.input.requestedRounds == null ? '' : String(result.input.requestedRounds)
    parseResult.value = result
    preview.value = null
  }
  catch (error) {
    parseResult.value = null
    toast.danger((error as Error)?.message || t('crm.prospecting.superSearchError'))
  }
  finally {
    parsing.value = false
  }
}

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
      <XhButton
        v-if="canExecute"
        variant="outline"
        :disabled="submitting"
        @click="submitPreview"
      >
        <XhButtonLabel>{{ submitting ? t('crm.prospecting.superSearchSubmitting') : t('crm.prospecting.superSearchPreviewButton') }}</XhButtonLabel>
      </XhButton>
    </header>

    <div class="objective-block">
      <XhFieldRoot class="objective-field">
        <XhFieldLabel>{{ t('crm.prospecting.superSearchObjective') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput
            v-model:value="objective"
            type="textarea"
            :rows="3"
            :placeholder="t('crm.prospecting.superSearchObjectivePlaceholder')"
          />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton
        v-if="canExecute"
        variant="subtle"
        :disabled="parsing"
        @click="parseObjective"
      >
        <XhButtonLabel>{{ parsing ? t('crm.prospecting.superSearchSubmitting') : t('crm.prospecting.superSearchParse') }}</XhButtonLabel>
      </XhButton>
    </div>

    <div v-if="parseResult" class="parse-result">
      <p>{{ t('crm.prospecting.superSearchParsed') }}</p>
      <ul>
        <li v-for="match in parseResult.matches" :key="match.field">
          {{ match.field }} ({{ match.marker }}): {{ match.values.join(', ') }}
        </li>
      </ul>
      <p v-if="parseResult.warnings.length > 0">
        {{ t('crm.prospecting.superSearchParseWarnings') }}: {{ parseResult.warnings.join(' ') }}
      </p>
    </div>

    <div class="search-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.campaign') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="campaignId" :options="campaignOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchMissionName') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="missionName" :max-length="160" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchMissionRequestKey') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="requestKey" :max-length="100" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchProducts') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="products" type="textarea" :rows="2" placeholder="industrial pump, valve" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchMarkets') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="markets" type="textarea" :rows="2" placeholder="DE, BR" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchCustomerTypes') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="customerTypes" type="textarea" :rows="2" placeholder="distributor, importer" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchIndustries') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="industries" type="textarea" :rows="2" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchProviders') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="providerCodes" type="textarea" :rows="2" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchExcludedTerms') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="excludedTerms" type="textarea" :rows="2" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchExcludedDomains') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="excludedDomains" type="textarea" :rows="2" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchDepth') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="depth" :options="depthOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.superSearchRounds') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="rounds" inputmode="numeric" :placeholder="`1–${maxRounds}`" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton
        v-if="canExecute"
        variant="outline"
        :disabled="savingMission || !campaignId"
        @click="saveMission"
      >
        <XhButtonLabel>{{ t('crm.prospecting.superSearchSaveMission') }}</XhButtonLabel>
      </XhButton>
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
        <XhButton
          v-if="canExecute && !mission.runId"
          variant="outline"
          @click="executeMission(mission)"
        >
          <XhButtonLabel>{{ t('crm.prospecting.superSearchMissionExecute') }}</XhButtonLabel>
        </XhButton>
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
  border: 1px solid hsl(var(--border));
  border-radius: var(--xh-radius-md);
  background: hsl(var(--muted));
}
.objective-block {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.objective-field {
  flex: 1 1 24rem;
}
.parse-result {
  font-size: 0.8rem;
}
.parse-result p {
  margin: 0.2rem 0;
}
.parse-result ul {
  margin: 0.2rem 0;
  padding-left: 1.2rem;
}
.search-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.read-only-notice {
  margin: 0;
  color: hsl(var(--muted-foreground));
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
  border-top: 1px solid hsl(var(--border));
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
  color: hsl(var(--muted-foreground));
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
  border-bottom: 1px solid hsl(var(--border));
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
  border-top: 1px solid hsl(var(--border));
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
  border-bottom: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.mission-main {
  display: grid;
  gap: 0.2rem;
  min-width: 0;
}
.mission-main small {
  color: hsl(var(--muted-foreground));
}
.empty-state {
  margin: 0;
  color: hsl(var(--muted-foreground));
}
</style>
