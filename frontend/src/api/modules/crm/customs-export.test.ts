import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmCustomsExportApi } from './customs-export'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmCustomsExportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.post.mockResolvedValue({})
  })

  it('routes validation, filtering and CSV export through the read-only query service', async () => {
    const input = { rowsJson: '[]', filter: { hsPrefix: '841370' } }
    await crmCustomsExportApi.validate(input)
    await crmCustomsExportApi.filter(input)
    await crmCustomsExportApi.export(input)

    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomsExportQuery/Validate', input, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomsExportQuery/Filter', input, undefined)
    expect(requestMocks.post).toHaveBeenCalledWith('/CrmCustomsExportQuery/Export', input, undefined)
  })

  it('never fetches an external customs source from the facade', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/api/modules/crm/customs-export.ts'), 'utf8')
    expect(source).toContain('createDynamicApiClient(\'CrmCustomsExportQuery\')')
    expect(source).not.toContain('fetch(')
  })
})
