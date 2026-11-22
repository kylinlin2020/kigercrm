<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import ExamCertificatePanel from './ExamCertificatePanel.vue'
import ExamPanel from './ExamPanel.vue'
import OutreachPanel from './OutreachPanel.vue'

defineOptions({ name: 'CrmProspectingTrainingPage' })

const { t } = useI18n()
</script>

<template>
  <main class="prospecting-subpage">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.prospecting.trainingTitle') }}</h1>
        <p>{{ t('crm.prospecting.trainingDescription') }}</p>
      </div>
    </header>
    <OutreachPanel />
    <ExamPanel />
    <ExamCertificatePanel />
  </main>
</template>

<style scoped>
.prospecting-subpage {
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.35rem;
}
.page-header p {
  margin: 0.25rem 0 0;
  color: var(--xh-text-muted, #777);
}
</style>
