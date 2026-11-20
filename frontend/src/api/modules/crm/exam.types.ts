import type { ApiId, DateTimeString } from '../../types'

export enum CrmExamPaperStatus {
  Draft = 0,
  Published = 1,
  Retired = 2,
}

export enum CrmExamAttemptStatus {
  InProgress = 0,
  Submitted = 1,
  Passed = 2,
  Failed = 3,
}

export interface CrmExamQuestionCreateDto {
  prompt: string
  type: number
  options: string[]
  correctAnswer: string
  explanation?: string | null
  points: number
}

/** Admin-facing question; the answer key is visible only to managers. */
export interface CrmExamQuestionDto extends CrmExamQuestionCreateDto {
  basicId: ApiId
}

/** Candidate-facing question; deliberately excludes correctAnswer and explanation. */
export interface CrmExamCandidateQuestionDto {
  basicId: ApiId
  prompt: string
  type: number
  options: string[]
  points: number
}

export interface CrmExamPaperCreateDto {
  name: string
  questionIds: ApiId[]
  durationMinutes: number
  passScore: number
  maxAttempts: number
}

export interface CrmExamPaperDto extends CrmExamPaperCreateDto {
  basicId: ApiId
  questionCount: number
  totalScore: number
  status: CrmExamPaperStatus
}

export interface CrmExamStartDto {
  paperId: ApiId
}

export interface CrmExamSubmissionDto {
  attemptId: ApiId
  answers: Record<string, string>
}

export interface CrmExamAnswerVerdictDto {
  questionId: ApiId
  correct: boolean
}

export interface CrmExamAttemptDto {
  basicId: ApiId
  paperId: ApiId
  paperName: string
  status: CrmExamAttemptStatus
  startedAtUtc: DateTimeString
  submittedAtUtc?: DateTimeString | null
  score: number
  totalScore: number
  questions: CrmExamCandidateQuestionDto[]
  verdicts: CrmExamAnswerVerdictDto[]
}
