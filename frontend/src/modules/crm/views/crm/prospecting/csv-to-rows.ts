import { parseCsvRows } from '~/utils'

export interface CsvToRowsOptions {
  /** Defaults to auto-detection: a first row of non-numeric strings is treated as a header. */
  hasHeader?: boolean
}

export interface CsvToRowsResult {
  rowsJson: string
  headers: string[]
  rowCount: number
}

const FIELD_SYNONYMS: Record<string, string> = {
  name: 'name',
  company: 'name',
  companyname: 'name',
  legalname: 'legalName',
  website: 'website',
  site: 'website',
  country: 'countryRegion',
  countryregion: 'countryRegion',
  region: 'countryRegion',
  industry: 'industry',
  sector: 'industry',
  source: 'source',
  notes: 'notes',
  note: 'notes',
}

function looksLikeHeader(cells: string[]): boolean {
  if (cells.length === 0)
    return false
  return cells.every(cell => cell.trim() !== '' && !/^-?\d+(?:\.\d+)?$/.test(cell.trim()))
}

/** Convert a CSV file into the JSON row payload accepted by CRM import APIs. */
export function csvToRows(text: string, options: CsvToRowsOptions = {}): CsvToRowsResult {
  const matrix = parseCsvRows(text)
  if (matrix.length === 0)
    throw new Error('CSV_EMPTY')
  const first = matrix[0]
  if (!first)
    throw new Error('CSV_EMPTY')
  const hasHeader = options.hasHeader ?? looksLikeHeader(first)
  const headers = hasHeader
    ? first.map(cell => cell.trim())
    : first.map((_, index) => `column${index + 1}`)
  const data = hasHeader ? matrix.slice(1) : matrix
  const rows = data.map((cells) => {
    const row: Record<string, string> = {}
    headers.forEach((header, index) => {
      if (header)
        row[header] = cells[index] ?? ''
    })
    return row
  })
  if (rows.length === 0)
    throw new Error('CSV_NO_DATA_ROWS')
  return { rowsJson: JSON.stringify(rows), headers, rowCount: rows.length }
}

/** Suggest source-column → CRM-field mappings for the customer import panel. */
export function suggestCustomerMapping(headers: string[]): Array<{ source: string, target: string }> {
  const result: Array<{ source: string, target: string }> = []
  for (const header of headers) {
    const key = header.replace(/[\s_-]/g, '').toLowerCase()
    const target = FIELD_SYNONYMS[key]
    if (target && !result.some(item => item.target === target)) {
      result.push({ source: header, target })
    }
  }
  return result
}
