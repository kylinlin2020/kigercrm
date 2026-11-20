export interface CrmCustomerImportInputDto {
  rowsJson: string
  mapping: Record<string, string>
  fileName?: string | null
}

export interface CrmCustomerImportPreviewRowDto {
  externalId: string
  name: string
  website?: string | null
  countryRegion?: string | null
  industry?: string | null
  errors: string[]
  duplicateReason?: string | null
}

export interface CrmCustomerImportPreviewDto {
  total: number
  valid: number
  duplicates: number
  rows: CrmCustomerImportPreviewRowDto[]
}

export interface CrmCustomerImportResultDto {
  total: number
  created: number
  invalid: number
  duplicates: number
  createdCustomerIds: string[]
  importHistoryId: string
}

export const CRM_CUSTOMER_IMPORT_FIELDS = [
  { value: 'name', label: 'Name' },
  { value: 'legalName', label: 'Legal name' },
  { value: 'website', label: 'Website' },
  { value: 'countryRegion', label: 'Country/region' },
  { value: 'industry', label: 'Industry' },
  { value: 'source', label: 'Source' },
  { value: 'notes', label: 'Notes' },
] as const
