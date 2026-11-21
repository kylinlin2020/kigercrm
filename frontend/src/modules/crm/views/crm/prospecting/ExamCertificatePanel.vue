<script setup lang="ts">
import type {
  CrmExamCertificateDto,
  CrmExamCertificateVerdictDto,
} from '@/api/modules/crm/exam-certificate.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmExamCertificateApi } from '@/api/modules/crm/exam-certificate'
import { toast } from '~/composables'

defineOptions({ name: 'CrmExamCertificatePanel' })

const { t } = useI18n()
const certificates = ref<CrmExamCertificateDto[]>([])
const attemptId = ref('')
const verifyNo = ref('')
const verifyHash = ref('')
const verdict = ref<CrmExamCertificateVerdictDto | null>(null)
const revokeReason = ref('')

function statusLabel(status: number) {
  return [
    t('crm.prospecting.certActive'),
    t('crm.prospecting.certRevoked'),
  ][status] ?? String(status)
}

async function reload() {
  try {
    certificates.value = await crmExamCertificateApi.mine()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.certError'))
  }
}

async function issue() {
  if (!attemptId.value)
    return
  try {
    await crmExamCertificateApi.issue(attemptId.value)
    toast.success(t('crm.prospecting.certIssued'))
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.certError'))
  }
}

async function verify() {
  try {
    verdict.value = await crmExamCertificateApi.verify({
      certificateNo: verifyNo.value,
      verificationHash: verifyHash.value,
    })
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.certError'))
  }
}

async function revoke(certificate: CrmExamCertificateDto) {
  try {
    await crmExamCertificateApi.revoke({
      certificateId: certificate.basicId,
      reason: revokeReason.value || 'manual revoke',
    })
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.certError'))
  }
}
</script>

<template>
  <section class="exam-cert">
    <header class="ec-head">
      <h3>{{ t('crm.prospecting.certTitle') }}</h3>
      <p>{{ t('crm.prospecting.certDescription') }}</p>
    </header>
    <div class="ec-actions">
      <label>{{ t('crm.prospecting.certAttemptId') }}<input v-model="attemptId" type="text"></label>
      <button type="button" :disabled="!attemptId" @click="issue">
        {{ t('crm.prospecting.certIssue') }}
      </button>
      <button type="button" @click="reload">
        {{ t('crm.prospecting.certRefresh') }}
      </button>
    </div>

    <ul class="ec-list">
      <li v-for="certificate in certificates" :key="certificate.basicId">
        <strong>{{ certificate.certificateNo }}</strong>
        <span>{{ certificate.score }}/{{ certificate.totalScore }} · {{ statusLabel(certificate.status) }}</span>
        <small>{{ certificate.verificationHash.slice(0, 16) }}…</small>
        <button v-if="certificate.status === 0" type="button" @click="revoke(certificate)">
          {{ t('crm.prospecting.certRevoke') }}
        </button>
      </li>
    </ul>

    <div class="ec-verify">
      <label>{{ t('crm.prospecting.certVerifyNo') }}<input v-model="verifyNo" type="text"></label>
      <label>{{ t('crm.prospecting.certVerifyHash') }}<input v-model="verifyHash" type="text"></label>
      <button type="button" :disabled="!verifyNo || !verifyHash" @click="verify">
        {{ t('crm.prospecting.certVerify') }}
      </button>
      <p v-if="verdict" :class="verdict.valid ? 'ec-valid' : 'ec-invalid'">
        {{ t('crm.prospecting.certVerdict', { result: verdict.reason }) }}
      </p>
    </div>
    <label class="ec-reason">
      {{ t('crm.prospecting.certRevokeReason') }}
      <input v-model="revokeReason" type="text">
    </label>
  </section>
</template>

<style scoped>
.exam-cert {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ec-actions,
.ec-verify {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.5rem;
}
.ec-actions label,
.ec-verify label,
.ec-reason {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ec-actions input,
.ec-verify input,
.ec-reason input {
  width: 100%;
  font: inherit;
}
.ec-list {
  list-style: none;
  margin: 0;
  padding: 0;
  font-size: 0.82rem;
}
.ec-list li {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.3rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
}
.ec-valid {
  color: var(--xh-success, #2a0);
}
.ec-invalid {
  color: var(--xh-danger, #c00);
}
</style>
