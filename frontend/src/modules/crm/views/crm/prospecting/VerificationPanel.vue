<script setup lang="ts">
import type {
  ApiId,
  CrmProspectingIdentityBootstrapDto,
  CrmProspectingIdentityMatchDto,
  CrmProspectingPageFetchDto,
  CrmProspectingQualificationVerdict,
  CrmProspectingVerificationReportDto,
  CrmProspectingVerificationState,
  CrmProspectingWebsiteAssessmentDto,
} from '@/api'
import { XhButton, XhButtonLabel } from '@xihan-ui/vue'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmProspectingApi, CrmProspectingIdentityBootstrapStage } from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingVerificationPanel' })

const props = defineProps<{
  candidateId: ApiId
  companyName: string
  website?: string | null
}>()

const { t } = useI18n()
const { hasPermission } = usePermission()
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))

const report = ref<CrmProspectingVerificationReportDto | null>(null)
const identityMatch = ref<CrmProspectingIdentityMatchDto | null>(null)
const websiteAssessment = ref<CrmProspectingWebsiteAssessmentDto | null>(null)
const bootstrap = ref<CrmProspectingIdentityBootstrapDto | null>(null)
const pageFetch = ref<CrmProspectingPageFetchDto | null>(null)
const busy = ref(false)

function verdictLabel(value: CrmProspectingQualificationVerdict) {
  return [
    t('crm.prospecting.verdictReady'),
    t('crm.prospecting.verdictNeedsReview'),
    t('crm.prospecting.verdictBlocked'),
  ][value] ?? String(value)
}

function stateLabel(value: CrmProspectingVerificationState) {
  return [
    t('crm.prospecting.verifyVerified'),
    t('crm.prospecting.verifyUnverified'),
    t('crm.prospecting.verifyInsufficient'),
    t('crm.prospecting.verifyConflict'),
  ][value] ?? String(value)
}

async function loadReport() {
  if (!props.candidateId) {
    report.value = null
    return
  }
  try {
    report.value = await crmProspectingApi.verificationReport(props.candidateId)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
}

watch(() => props.candidateId, () => {
  void loadReport()
}, { immediate: true })

async function resolveIdentity() {
  if (!canExecute.value || busy.value) {
    return
  }
  busy.value = true
  try {
    identityMatch.value = await crmProspectingApi.resolveIdentity(props.companyName)
    toast.success(identityMatch.value
      ? t('crm.prospecting.verifyIdentityFound', { lei: identityMatch.value.lei })
      : t('crm.prospecting.verifyIdentityNone'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
  finally {
    busy.value = false
  }
}

async function assessWebsite() {
  if (!canExecute.value || busy.value || !props.website) {
    return
  }
  busy.value = true
  try {
    websiteAssessment.value = await crmProspectingApi.assessWebsite({
      companyName: props.companyName,
      candidateUrl: props.website,
      sourceProviderCode: 'manual-review',
      sourceKind: 3,
    })
    toast.success(t('crm.prospecting.verifyWebsiteReason', { reason: websiteAssessment.value.reasonCode }))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
  finally {
    busy.value = false
  }
}

async function bootstrapIdentity() {
  if (!canExecute.value || busy.value) {
    return
  }
  busy.value = true
  try {
    bootstrap.value = await crmProspectingApi.bootstrapIdentity(props.companyName)
    if (bootstrap.value.stage === CrmProspectingIdentityBootstrapStage.ExactRegistryMatch) {
      identityMatch.value = bootstrap.value.authoritativeMatch ?? null
    }
    toast.success(t('crm.prospecting.verifyBootstrapStage', { stage: bootstrapStageLabel(bootstrap.value.stage) }))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
  finally {
    busy.value = false
  }
}

function bootstrapStageLabel(value: CrmProspectingIdentityBootstrapStage) {
  return [
    t('crm.prospecting.verifyStageUnresolved'),
    t('crm.prospecting.verifyStageAmbiguous'),
    t('crm.prospecting.verifyStageExact'),
  ][value] ?? String(value)
}

async function probeWebsite() {
  if (!canExecute.value || busy.value || !props.website) {
    return
  }
  busy.value = true
  try {
    pageFetch.value = await crmProspectingApi.probeWebsite({
      candidateId: props.candidateId,
      companyName: props.companyName,
      candidateUrl: props.website,
    })
    toast.success(t('crm.prospecting.verifyProbeReason', { reason: pageFetch.value.reasonCode }))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
  finally {
    busy.value = false
  }
}

async function buildReport() {
  if (!canExecute.value || busy.value) {
    return
  }
  busy.value = true
  try {
    report.value = await crmProspectingApi.buildVerificationReport({
      candidateId: props.candidateId,
      identityMatch: identityMatch.value,
    })
    toast.success(t('crm.prospecting.verifyReportReady'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.verifyError'))
  }
  finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="verification-panel">
    <header class="section-header">
      <div>
        <h3>{{ t('crm.prospecting.verifyTitle') }}</h3>
        <p>{{ t('crm.prospecting.verifyDescription') }}</p>
      </div>
      <div class="verify-actions">
        <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="bootstrapIdentity">
          <XhButtonLabel>{{ t('crm.prospecting.verifyBootstrap') }}</XhButtonLabel>
        </XhButton>
        <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="resolveIdentity">
          <XhButtonLabel>{{ t('crm.prospecting.verifyResolveIdentity') }}</XhButtonLabel>
        </XhButton>
        <XhButton v-if="canExecute" variant="outline" :disabled="busy || !website" @click="assessWebsite">
          <XhButtonLabel>{{ t('crm.prospecting.verifyAssessWebsite') }}</XhButtonLabel>
        </XhButton>
        <XhButton v-if="canExecute" variant="outline" :disabled="busy || !website" @click="probeWebsite">
          <XhButtonLabel>{{ t('crm.prospecting.verifyProbeWebsite') }}</XhButtonLabel>
        </XhButton>
        <XhButton v-if="canExecute" variant="outline" :disabled="busy" @click="buildReport">
          <XhButtonLabel>{{ t('crm.prospecting.verifyBuildReport') }}</XhButtonLabel>
        </XhButton>
      </div>
    </header>

    <p v-if="websiteAssessment" class="verify-note">
      {{ t('crm.prospecting.verifyWebsiteReason', { reason: websiteAssessment.reasonCode }) }}
    </p>

    <div v-if="bootstrap" class="verify-note">
      <strong>{{ t('crm.prospecting.verifyBootstrapStage', { stage: bootstrapStageLabel(bootstrap.stage) }) }}</strong>
      <span v-for="candidate in bootstrap.candidates" :key="candidate.lei">
        {{ candidate.legalName }} ({{ candidate.lei }})
      </span>
      <span v-for="step in bootstrap.steps" :key="step.stage + step.reasonCode">
        {{ step.stage }}: {{ step.reasonCode }} = {{ step.detail }}
      </span>
    </div>

    <div v-if="pageFetch" class="verify-note">
      {{ t('crm.prospecting.verifyProbeResult', {
        status: pageFetch.statusCode,
        bytes: pageFetch.byteCount,
        reason: pageFetch.reasonCode,
      }) }}
      <span v-if="pageFetch.evidencePersisted">{{ t('crm.prospecting.verifyEvidenceSaved') }}</span>
      <code v-if="pageFetch.bodyHash">{{ pageFetch.bodyHash.slice(0, 12) }}</code>
      <span v-if="pageFetch.title">{{ pageFetch.title }}</span>
    </div>

    <template v-if="report">
      <div class="verify-summary">
        <span class="status-pill">{{ verdictLabel(report.verdict) }}</span>
        <span>{{ report.summary }}</span>
      </div>
      <ul class="verify-fields">
        <li v-for="field in report.fields" :key="field.field">
          <strong>{{ field.field }}</strong>
          <span>{{ stateLabel(field.state) }} · {{ field.reasonCode }}</span>
          <small>{{ field.authority }}</small>
        </li>
      </ul>
      <div v-if="report.blockers.length > 0" class="verify-note">
        {{ t('crm.prospecting.verifyBlockers') }}: {{ report.blockers.join(' ') }}
      </div>
      <div v-if="report.missingFields.length > 0" class="verify-note">
        {{ t('crm.prospecting.verifyMissing') }}: {{ report.missingFields.join(', ') }}
      </div>
      <div v-if="report.recommendedActions.length > 0" class="verify-note">
        {{ t('crm.prospecting.verifyActions') }}: {{ report.recommendedActions.join(' ') }}
      </div>
    </template>
    <p v-else class="verify-note">
      {{ t('crm.prospecting.verifyEmpty') }}
    </p>
  </section>
</template>

<style scoped>
.verification-panel {
  display: grid;
  gap: 0.5rem;
  border-top: 1px solid hsl(var(--border));
  padding-top: 0.6rem;
}
.verify-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.verify-note {
  margin: 0;
  color: hsl(var(--muted-foreground));
  font-size: 0.8rem;
}
.verify-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.6rem;
  font-size: 0.85rem;
}
.verify-fields {
  display: grid;
  gap: 0.3rem;
  margin: 0;
  padding-left: 1.2rem;
  font-size: 0.8rem;
}
.verify-fields li {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.verify-fields small {
  color: hsl(var(--muted-foreground));
}
</style>
