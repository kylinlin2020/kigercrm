import type {
  CrmDashboardOverviewDto,
  CrmDashboardReportDto,
} from './dashboard.types'
import { createDynamicApiClient } from '@/api/base'

const api = createDynamicApiClient('CrmDashboardQuery')

/** Read-only dashboard reports; tenant and user data scope are enforced by the backend. */
export const crmDashboardApi = {
  overview() {
    return api.get<CrmDashboardOverviewDto>('Overview')
  },
  leadFunnel() {
    return api.get<CrmDashboardReportDto>('LeadFunnel')
  },
  prospectSources() {
    return api.get<CrmDashboardReportDto>('ProspectSources')
  },
  opportunityStages() {
    return api.get<CrmDashboardReportDto>('OpportunityStages')
  },
}
