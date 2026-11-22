<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import BusinessCardImportPanel from './BusinessCardImportPanel.vue'
import CustomerImportPanel from './CustomerImportPanel.vue'
import CustomsExportPanel from './CustomsExportPanel.vue'
import TradeDocumentImportPanel from './TradeDocumentImportPanel.vue'

defineOptions({ name: 'CrmProspectingDataPage' })

const { t } = useI18n()
</script>

<template>
  <main class="prospecting-subpage">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.prospecting.dataTitle') }}</h1>
        <p>{{ t('crm.prospecting.dataDescription') }}</p>
      </div>
    </header>
    <BusinessCardImportPanel />
    <TradeDocumentImportPanel />
    <CustomerImportPanel />
    <CustomsExportPanel />
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
