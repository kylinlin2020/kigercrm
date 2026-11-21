import type { ApiId } from '../../types'
import type {
  CrmExamAttemptDto,
  CrmExamPaperCreateDto,
  CrmExamPaperDto,
  CrmExamQuestionCreateDto,
  CrmExamQuestionDto,
  CrmExamStartDto,
  CrmExamSubmissionDto,
} from './exam.types'
import { createDynamicApiClient } from '../../base'

const api = createDynamicApiClient('CrmExam')

/** Sales training exams: question bank, publishing, and bounded attempts. */
export const crmExamApi = {
  createQuestion(input: CrmExamQuestionCreateDto) {
    return api.post<CrmExamQuestionDto, CrmExamQuestionCreateDto>('Question', input)
  },
  questions() {
    return api.get<CrmExamQuestionDto[]>('Questions')
  },
  questionsByCategory(category: string) {
    return api.get<CrmExamQuestionDto[]>('QuestionsByCategory', { category })
  },
  categories() {
    return api.get<string[]>('Categories')
  },
  createPaper(input: CrmExamPaperCreateDto) {
    return api.post<CrmExamPaperDto, CrmExamPaperCreateDto>('Paper', input)
  },
  publishPaper(paperId: ApiId) {
    return api.post<CrmExamPaperDto, undefined>('PublishPaper', undefined, { params: { paperId } })
  },
  papers() {
    return api.get<CrmExamPaperDto[]>('Papers')
  },
  start(input: CrmExamStartDto) {
    return api.post<CrmExamAttemptDto, CrmExamStartDto>('Start', input)
  },
  submit(input: CrmExamSubmissionDto) {
    return api.post<CrmExamAttemptDto, CrmExamSubmissionDto>('Submit', input)
  },
  myAttempts() {
    return api.get<CrmExamAttemptDto[]>('MyAttempts')
  },
}
