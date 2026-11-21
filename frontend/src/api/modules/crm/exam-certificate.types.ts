import type { ApiId, DateTimeString } from '../../types'

export enum CrmExamCertificateStatus {
  Active = 0,
  Revoked = 1,
}

export interface CrmExamCertificateDto {
  basicId: ApiId
  certificateNo: string
  attemptId: ApiId
  paperId: ApiId
  holderUserId: ApiId
  score: number
  totalScore: number
  issuedAtUtc: DateTimeString
  validUntilUtc: DateTimeString
  verificationHash: string
  status: CrmExamCertificateStatus
}

export interface CrmExamCertificateVerifyDto {
  certificateNo: string
  verificationHash: string
}

export interface CrmExamCertificateVerdictDto {
  valid: boolean
  reason: string
  certificate?: CrmExamCertificateDto | null
}

export interface CrmExamCertificateRevokeDto {
  certificateId: ApiId
  reason: string
}
