<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import CrmDashboardPanel from './CrmDashboardPanel.vue'

defineOptions({ name: 'CrmProspectingDashboardPage' })

const { t } = useI18n()
</script>

<template>
  <main class="prospecting-subpage">
    <header class="page-header">
      <div>
        <h1>{{ t('menu.crm_prospecting_dashboard') }}</h1>
        <p>{{ t('crm.prospecting.dashboardDescription') }}</p>
      </div>
    </header>
    <CrmDashboardPanel />
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
