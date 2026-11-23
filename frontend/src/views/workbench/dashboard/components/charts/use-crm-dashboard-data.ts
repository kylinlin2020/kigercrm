import type { InjectionKey, Ref } from 'vue'
import type {
  CrmDashboardOverviewDto,
  CrmDashboardReportDto,
} from '@/api/modules/crm/dashboard.types'
import { computed, inject, onMounted, ref } from 'vue'
import { crmDashboardApi } from '@/api/modules/crm/dashboard'

/**
 * Shared CRM dashboard data for the workbench widgets. A single request fans out to all widgets;
 * the range (UTC ISO strings) is shared so team-ranking and follow-up health stay consistent.
 */
export interface CrmDashboardData {
  loading: Ref<boolean>
  overview: Ref<CrmDashboardOverviewDto | null>
  funnel: Ref<CrmDashboardReportDto | null>
  sources: Ref<CrmDashboardReportDto | null>
  stages: Ref<CrmDashboardReportDto | null>
  team: Ref<CrmDashboardReportDto | null>
  followUp: Ref<CrmDashboardReportDto | null>
  fromUtc: Ref<string | undefined>
  toUtc: Ref<string | undefined>
  load: () => Promise<void>
}

export const CRM_DASHBOARD_DATA: InjectionKey<CrmDashboardData> = Symbol('CrmDashboardData')

export function createCrmDashboardData(): CrmDashboardData {
  const loading = ref(false)
  const overview = ref<CrmDashboardOverviewDto | null>(null)
  const funnel = ref<CrmDashboardReportDto | null>(null)
  const sources = ref<CrmDashboardReportDto | null>(null)
  const stages = ref<CrmDashboardReportDto | null>(null)
  const team = ref<CrmDashboardReportDto | null>(null)
  const followUp = ref<CrmDashboardReportDto | null>(null)
  const fromUtc = ref<string | undefined>(undefined)
  const toUtc = ref<string | undefined>(undefined)

  async function load() {
    loading.value = true
    try {
      const [summary, leadReport, sourceReport, stageReport, teamReport, healthReport] = await Promise.all([
        crmDashboardApi.overview(),
        crmDashboardApi.leadFunnel(),
        crmDashboardApi.prospectSources(),
        crmDashboardApi.opportunityStages(),
        crmDashboardApi.teamRanking(fromUtc.value, toUtc.value),
        crmDashboardApi.followUpHealth(fromUtc.value, toUtc.value),
      ])
      overview.value = summary
      funnel.value = leadReport
      sources.value = sourceReport
      stages.value = stageReport
      team.value = teamReport
      followUp.value = healthReport
    }
    finally {
      loading.value = false
    }
  }

  return { loading, overview, funnel, sources, stages, team, followUp, fromUtc, toUtc, load }
}

/** Consume the shared data from the nearest provider; widgets must be mounted under it. */
export function useCrmDashboardData(): CrmDashboardData {
  const data = inject(CRM_DASHBOARD_DATA)
  if (!data)
    throw new Error('CRM dashboard widget must be mounted under the CRM data provider')
  onMounted(async () => {
    await data.load()
  })
  return data
}

/** Expose loading state for the range picker in the provider scope. */
export function useCrmDashboardLoading() {
  const data = useCrmDashboardData()
  return computed(() => data.loading.value)
}
