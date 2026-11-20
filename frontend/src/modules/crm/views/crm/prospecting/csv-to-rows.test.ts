import { describe, expect, it } from 'vitest'
import { csvToRows, suggestCustomerMapping } from './csv-to-rows'

describe('csvToRows', () => {
  it('uses the header row and preserves quoted commas and escaped quotes', () => {
    const result = csvToRows('company,website\n"Acme, Inc.","https://a.example/?x=""y"""\nBeta,https://b.example')
    expect(result.headers).toEqual(['company', 'website'])
    expect(result.rowCount).toBe(2)
    expect(JSON.parse(result.rowsJson)).toEqual([
      { company: 'Acme, Inc.', website: 'https://a.example/?x="y"' },
      { company: 'Beta', website: 'https://b.example' },
    ])
  })

  it('handles CRLF and BOM, and skips blank lines', () => {
    const result = csvToRows('\uFEFFname,region\r\nAcme,DE\r\n\r\nBeta,US\r\n')
    expect(result.headers).toEqual(['name', 'region'])
    expect(result.rowCount).toBe(2)
  })

  it('generates column names when no header is present', () => {
    const result = csvToRows('Acme,DE\nBeta,US', { hasHeader: false })
    expect(result.headers).toEqual(['column1', 'column2'])
    expect(JSON.parse(result.rowsJson)[0]).toEqual({ column1: 'Acme', column2: 'DE' })
  })

  it('rejects empty or header-only files', () => {
    expect(() => csvToRows('')).toThrow('CSV_EMPTY')
    expect(() => csvToRows('name,region\n')).toThrow('CSV_NO_DATA_ROWS')
  })
})

describe('suggestCustomerMapping', () => {
  it('maps common source headers to customer fields without duplicates', () => {
    expect(suggestCustomerMapping(['Company Name', 'site', 'region', 'sector', 'note', 'unknown'])).toEqual([
      { source: 'Company Name', target: 'name' },
      { source: 'site', target: 'website' },
      { source: 'region', target: 'countryRegion' },
      { source: 'sector', target: 'industry' },
      { source: 'note', target: 'notes' },
    ])
  })
})
