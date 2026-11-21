import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmExamCertificateApi } from './exam-certificate'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmExamCertificateApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes issue, list, verify and revoke through the certificate service', async () => {
    await crmExamCertificateApi.issue('11')
    await crmExamCertificateApi.mine()
    await crmExamCertificateApi.verify({ certificateNo: 'EX-1', verificationHash: 'abc' })
    await crmExamCertificateApi.revoke({ certificateId: '9', reason: 'fraud' })

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExamCertificate/Issue',
      undefined,
      { params: { attemptId: '11' } },
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmExamCertificate/Mine', {})
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExamCertificate/Verify',
      { certificateNo: 'EX-1', verificationHash: 'abc' },
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExamCertificate/Revoke',
      { certificateId: '9', reason: 'fraud' },
      undefined,
    )
  })
})
