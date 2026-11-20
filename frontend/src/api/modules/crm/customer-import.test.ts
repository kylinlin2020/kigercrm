import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmCustomerImportApi } from './customer-import'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmCustomerImportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.post.mockResolvedValue({})
  })

  it('routes preview and import through the customer import service', async () => {
    const input = {
      rowsJson: '[]',
      mapping: { company: 'name' },
      fileName: 'customers.json',
    }
    await crmCustomerImportApi.preview(input)
    await crmCustomerImportApi.import(input)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerImport/Preview', input, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomerImport/Import', input, undefined)
  })

  it('declares the supported customer target fields', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/api/modules/crm/customer-import.types.ts'), 'utf8')
    for (const field of ['name', 'legalName', 'website', 'countryRegion', 'industry', 'source', 'notes']) {
      expect(source).toContain(`'${field}'`)
    }
    expect(source).not.toContain('correctAnswer')
  })
})
