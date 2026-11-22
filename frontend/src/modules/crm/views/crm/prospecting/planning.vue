<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useProspectingWorkspaceStore } from './prospecting-workspace'
import ProviderCredentialsPanel from './ProviderCredentialsPanel.vue'
import SchedulePanel from './SchedulePanel.vue'
import SuperSearchPreviewPanel from './SuperSearchPreviewPanel.vue'

defineOptions({ name: 'CrmProspectingPlanningPage' })

const { t } = useI18n()
const workspace = useProspectingWorkspaceStore()

function notifyWorkspace() {
  workspace.requestRefresh()
}
</script>

<template>
  <main class="prospecting-subpage">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.prospecting.planningTitle') }}</h1>
        <p>{{ t('crm.prospecting.planningDescription') }}</p>
      </div>
    </header>
    <ProviderCredentialsPanel />
    <SuperSearchPreviewPanel @run-created="notifyWorkspace" />
    <SchedulePanel @schedule-changed="notifyWorkspace" />
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
  color: hsl(var(--muted-foreground));
}
</style>
