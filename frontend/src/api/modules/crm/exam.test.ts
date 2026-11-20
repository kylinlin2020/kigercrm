import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { crmExamApi } from './exam'

const requestMocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../../request', () => ({ requestClient: requestMocks }))

describe('crmExamApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    requestMocks.get.mockResolvedValue([])
    requestMocks.post.mockResolvedValue({})
  })

  it('routes exam management and attempt operations through the Exam service', async () => {
    await crmExamApi.createQuestion({ prompt: 'Q', type: 0, options: ['A', 'B'], correctAnswer: 'A', points: 1 })
    await crmExamApi.questions()
    await crmExamApi.createPaper({ name: 'P', questionIds: ['1'], durationMinutes: 30, passScore: 1, maxAttempts: 2 })
    await crmExamApi.publishPaper('7')
    await crmExamApi.papers()
    await crmExamApi.start({ paperId: '7' })
    await crmExamApi.submit({ attemptId: '9', answers: { 1: 'A' } })
    await crmExamApi.myAttempts()

    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExam/Question',
      expect.objectContaining({ prompt: 'Q' }),
      undefined,
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExam/PublishPaper',
      undefined,
      expect.objectContaining({ params: { paperId: '7' } }),
    )
    expect(requestMocks.post).toHaveBeenCalledWith(
      '/CrmExam/Submit',
      { attemptId: '9', answers: { 1: 'A' } },
      undefined,
    )
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmExam/Questions', {})
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmExam/Papers', {})
    expect(requestMocks.get).toHaveBeenCalledWith('/CrmExam/MyAttempts', {})
  })

  it('keeps candidate wire types free of the answer key', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/api/modules/crm/exam.types.ts'), 'utf8')
    const candidate = source.slice(
      source.indexOf('interface CrmExamCandidateQuestionDto'),
      source.indexOf('interface CrmExamPaperCreateDto'),
    )
    expect(candidate).not.toContain('correctAnswer')
    expect(candidate).not.toContain('explanation')
  })
})
