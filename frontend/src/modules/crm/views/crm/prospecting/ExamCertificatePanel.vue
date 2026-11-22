<script setup lang="ts">
import type {
  CrmExamCertificateDto,
  CrmExamCertificateVerdictDto,
} from '@/api/modules/crm/exam-certificate.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmExamCertificateApi } from '@/api/modules/crm/exam-certificate'
import { XInput } from '~/components'
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
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.certAttemptId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="attemptId" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!attemptId" @click="issue">
        <XhButtonLabel>{{ t('crm.prospecting.certIssue') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" @click="reload">
        <XhButtonLabel>{{ t('crm.prospecting.certRefresh') }}</XhButtonLabel>
      </XhButton>
    </div>

    <ul class="ec-list">
      <li v-for="certificate in certificates" :key="certificate.basicId">
        <strong>{{ certificate.certificateNo }}</strong>
        <span>{{ certificate.score }}/{{ certificate.totalScore }} · {{ statusLabel(certificate.status) }}</span>
        <small>{{ certificate.verificationHash.slice(0, 16) }}…</small>
        <XhButton v-if="certificate.status === 0" variant="subtle" @click="revoke(certificate)">
          <XhButtonLabel>{{ t('crm.prospecting.certRevoke') }}</XhButtonLabel>
        </XhButton>
      </li>
    </ul>

    <div class="ec-verify">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.certVerifyNo') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="verifyNo" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.prospecting.certVerifyHash') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="verifyHash" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!verifyNo || !verifyHash" @click="verify">
        <XhButtonLabel>{{ t('crm.prospecting.certVerify') }}</XhButtonLabel>
      </XhButton>
      <p v-if="verdict" :class="verdict.valid ? 'ec-valid' : 'ec-invalid'">
        {{ t('crm.prospecting.certVerdict', { result: verdict.reason }) }}
      </p>
    </div>
    <XhFieldRoot class="ec-reason">
      <XhFieldLabel>{{ t('crm.prospecting.certRevokeReason') }}</XhFieldLabel>
      <XhFieldControl>
        <XInput v-model:value="revokeReason" />
      </XhFieldControl>
    </XhFieldRoot>
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
  border-top: 1px solid hsl(var(--border));
}
.ec-valid {
  color: hsl(var(--success));
}
.ec-invalid {
  color: hsl(var(--destructive));
}
</style>
