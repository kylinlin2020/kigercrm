import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmBusinessCardImportApi } from './business-card-import'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmBusinessCardImportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes preview, import, decisions and batch reads through the import service', async () => {
    await crmBusinessCardImportApi.preview({ rowsJson: '[]' })
    await crmBusinessCardImportApi.import({ rowsJson: '[]' })
    await crmBusinessCardImportApi.confirm({ rowId: '3' })
    await crmBusinessCardImportApi.reject({ rowId: '4', reason: 'blurry' })
    await crmBusinessCardImportApi.rows('batch-1')
    await crmBusinessCardImportApi.batches()

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmBusinessCardImport/Preview',
      { rowsJson: '[]' },
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmBusinessCardImport/Confirm',
      { rowId: '3' },
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmBusinessCardImport/Reject',
      { rowId: '4', reason: 'blurry' },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmBusinessCardImport/Rows', { params: { batchId: 'batch-1' } })
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmBusinessCardImport/Batches', {})
  })

  it('keeps the import payload as caller-supplied OCR text, never an external client', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/api/modules/crm/business-card-import.ts'),
      'utf8',
    )
    expect(source).toContain('createDynamicApiClient(\'CrmBusinessCardImport\')')
    expect(source).not.toContain('fetch(')
  })
})
