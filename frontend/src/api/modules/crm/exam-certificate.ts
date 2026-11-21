import type {
  CrmExamCertificateDto,
  CrmExamCertificateRevokeDto,
  CrmExamCertificateVerdictDto,
  CrmExamCertificateVerifyDto,
} from './exam-certificate.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmExamCertificate')

/** Exam certificates: issue from a passed attempt, list, verify and revoke. */
export const crmExamCertificateApi = {
  issue(attemptId: string) {
    return api.post<CrmExamCertificateDto, undefined>('Issue', undefined, { params: { attemptId } })
  },
  mine() {
    return api.get<CrmExamCertificateDto[]>('Mine')
  },
  verify(input: CrmExamCertificateVerifyDto) {
    return api.post<CrmExamCertificateVerdictDto, CrmExamCertificateVerifyDto>('Verify', input)
  },
  revoke(input: CrmExamCertificateRevokeDto) {
    return api.post<CrmExamCertificateDto, CrmExamCertificateRevokeDto>('Revoke', input)
  },
}
