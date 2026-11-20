import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmTradeDocumentImportApi } from './trade-document-import'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmTradeDocumentImportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes preview, import, mapped confirmation and order-line lookup', async () => {
    await crmTradeDocumentImportApi.preview({ rowsJson: '[]', salesOrderId: '9' })
    await crmTradeDocumentImportApi.import({ rowsJson: '[]' })
    await crmTradeDocumentImportApi.confirm({ rowId: '5', salesOrderId: '9', lines: [{ orderLineId: '7', quantity: 2 }] })
    await crmTradeDocumentImportApi.reject({ rowId: '6', reason: 'blurry' })
    await crmTradeDocumentImportApi.rows('batch-1')
    await crmTradeDocumentImportApi.batches()
    await crmTradeDocumentImportApi.orderLines('9')

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmTradeDocumentImport/Preview',
      { rowsJson: '[]', salesOrderId: '9' },
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmTradeDocumentImport/Confirm',
      { rowId: '5', salesOrderId: '9', lines: [{ orderLineId: '7', quantity: 2 }] },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith(
      '/CrmTradeDocumentImport/OrderLines',
      { params: { salesOrderId: '9' } },
    )
  })

  it('never calls an external OCR client from the facade', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/api/modules/crm/trade-document-import.ts'), 'utf8')
    expect(source).toContain('createDynamicApiClient(\'CrmTradeDocumentImport\')')
    expect(source).not.toContain('fetch(')
  })
})
