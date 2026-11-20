export interface CrmCustomsFilterDto {
  industry?: string | null
  hsPrefix?: string | null
  countryCode?: string | null
}

export interface CrmCustomsInputDto {
  rowsJson: string
  filter?: CrmCustomsFilterDto | null
}

export interface CrmCustomsRowDto {
  companyName: string
  countryCode: string
  hsCode: string
  industry: string
  exportDate: string
  amount: number
  productDescription?: string | null
}

export interface CrmCustomsValidationRowDto {
  companyName: string
  hsCode: string
  errors: string[]
}

export interface CrmCustomsValidationDto {
  total: number
  valid: number
  rows: CrmCustomsValidationRowDto[]
}

export interface CrmCustomsFilterResultDto {
  total: number
  matched: number
  rows: CrmCustomsRowDto[]
}

export interface CrmCustomsExportResultDto {
  exported: number
  csv: string
}
