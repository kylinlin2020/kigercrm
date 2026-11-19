import type { DateTimeString } from '../../types'

export interface CrmDashboardBucketDto {
  key: string
  label: string
  count: number
  amount: number
  sharePercent: number
}

export interface CrmDashboardReportDto {
  buckets: CrmDashboardBucketDto[]
  total: number
  totalAmount: number
}

export interface CrmDashboardOverviewDto {
  leadsTotal: number
  opportunitiesTotal: number
  opportunityAmount: number
  prospectsTotal: number
  runsTotal: number
  activeRuns: number
  generatedAtUtc: DateTimeString
}

export type CrmDashboardReportKey = 'LeadFunnel' | 'ProspectSources' | 'OpportunityStages'
