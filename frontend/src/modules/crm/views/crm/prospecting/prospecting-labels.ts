import { useI18n } from 'vue-i18n'

/** Shared view labels: the page and extracted tables must render identical status text. */
export function useProspectingLabels() {
  const { t } = useI18n()

  function candidateStatus(value: number) {
    return [
      t('crm.prospecting.pendingReview'),
      t('crm.prospecting.duplicate'),
      t('crm.prospecting.approved'),
      t('crm.prospecting.rejected'),
      t('crm.prospecting.convertingStatus'),
      t('crm.prospecting.convertedStatus'),
    ][value] ?? String(value)
  }

  return { candidateStatus }
}
