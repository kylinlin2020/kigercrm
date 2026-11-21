<script setup lang="ts">
import type { CrmExamAttemptDto, CrmExamPaperDto, CrmExamQuestionDto } from '@/api/modules/crm/exam.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmExamApi } from '@/api/modules/crm/exam'
import {

  CrmExamQuestionType,
} from '@/api/modules/crm/exam.types'
import { toast } from '~/composables'

defineOptions({ name: 'CrmExamPanel' })

const { t } = useI18n()

const questions = ref<CrmExamQuestionDto[]>([])
const papers = ref<CrmExamPaperDto[]>([])
const attempts = ref<CrmExamAttemptDto[]>([])
const active = ref<CrmExamAttemptDto | null>(null)
const answers = ref<Record<string, string>>({})

const newQuestion = ref({ prompt: '', type: CrmExamQuestionType.SingleChoice, category: '', options: 'A, B', correctAnswer: 'A', correctAnswers: 'A, B', points: 1 })
const newPaper = ref({ name: '', questionIds: '', selectionRules: '', randomizeSelection: false, selectionSeed: '42', durationMinutes: 30, passScore: 60, maxAttempts: 3 })

function statusLabel(status: number) {
  return [
    t('crm.prospecting.examStatusInProgress'),
    t('crm.prospecting.examStatusSubmitted'),
    t('crm.prospecting.examStatusPassed'),
    t('crm.prospecting.examStatusFailed'),
  ][status] ?? String(status)
}

async function reload() {
  try {
    const [q, p, a] = await Promise.all([
      crmExamApi.questions(),
      crmExamApi.papers(),
      crmExamApi.myAttempts(),
    ])
    questions.value = q
    papers.value = p
    attempts.value = a
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}

async function addQuestion() {
  try {
    await crmExamApi.createQuestion({
      prompt: newQuestion.value.prompt,
      type: newQuestion.value.type,
      category: newQuestion.value.category || null,
      options: newQuestion.value.options.split(',').map(option => option.trim()).filter(Boolean),
      correctAnswer: newQuestion.value.correctAnswer.trim(),
      correctAnswers: newQuestion.value.type === CrmExamQuestionType.MultipleChoice
        ? newQuestion.value.correctAnswers.split(',').map(value => value.trim()).filter(Boolean)
        : [],
      points: Number(newQuestion.value.points),
    })
    newQuestion.value.prompt = ''
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}

async function addPaper() {
  try {
    await crmExamApi.createPaper({
      name: newPaper.value.name,
      questionIds: newPaper.value.questionIds.split(',').map(id => id.trim()).filter(Boolean),
      selectionRules: newPaper.value.selectionRules.split(';').map((pair) => {
        const [category, count] = pair.split(':').map(value => value.trim())
        return { category, count: Number(count) }
      }).filter(rule => rule.category && rule.count > 0).map(rule => ({ category: rule.category as string, count: rule.count })),
      randomizeSelection: newPaper.value.randomizeSelection,
      selectionSeed: newPaper.value.selectionSeed ? Number(newPaper.value.selectionSeed) : null,
      durationMinutes: Number(newPaper.value.durationMinutes),
      passScore: Number(newPaper.value.passScore),
      maxAttempts: Number(newPaper.value.maxAttempts),
    })
    newPaper.value.name = ''
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}

async function publish(paperId: string) {
  try {
    await crmExamApi.publishPaper(paperId)
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}

async function start(paperId: string) {
  try {
    active.value = await crmExamApi.start({ paperId })
    answers.value = {}
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}

function toggleMulti(questionId: string, option: string, event: Event) {
  const selected = new Set((answers.value[questionId] ?? '').split(',').filter(Boolean))
  if ((event.target as HTMLInputElement).checked)
    selected.add(option)
  else selected.delete(option)
  answers.value[questionId] = [...selected].join(',')
}

function isMultiSelected(questionId: string, option: string) {
  return (answers.value[questionId] ?? '').split(',').includes(option)
}

async function submit() {
  if (!active.value)
    return
  try {
    active.value = await crmExamApi.submit({ attemptId: active.value.basicId, answers: answers.value })
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.prospecting.examError'))
  }
}
</script>

<template>
  <section class="crm-exam">
    <header class="ce-head">
      <h3>{{ t('crm.prospecting.examTitle') }}</h3>
      <p>{{ t('crm.prospecting.examDescription') }}</p>
    </header>

    <div class="ce-grid">
      <div class="ce-block">
        <h4>{{ t('crm.prospecting.examQuestions') }}</h4>
        <label>{{ t('crm.prospecting.examPrompt') }}<input v-model="newQuestion.prompt" type="text"></label>
        <label>{{ t('crm.prospecting.examCategory') }}<input v-model="newQuestion.category" type="text"></label>
        <label>{{ t('crm.prospecting.examType') }}
          <select v-model.number="newQuestion.type">
            <option :value="0">{{ t('crm.prospecting.examTypeSingle') }}</option>
            <option :value="1">{{ t('crm.prospecting.examTypeTrueFalse') }}</option>
            <option :value="2">{{ t('crm.prospecting.examTypeMulti') }}</option>
          </select>
        </label>
        <label>{{ t('crm.prospecting.examOptions') }}<input v-model="newQuestion.options" type="text"></label>
        <label v-if="newQuestion.type === 2">{{ t('crm.prospecting.examCorrectMulti') }}<input v-model="newQuestion.correctAnswers" type="text"></label>
        <label v-else>{{ t('crm.prospecting.examCorrect') }}<input v-model="newQuestion.correctAnswer" type="text"></label>
        <label>{{ t('crm.prospecting.examPoints') }}<input v-model.number="newQuestion.points" type="number" min="1"></label>
        <button type="button" :disabled="!newQuestion.prompt" @click="addQuestion">
          {{ t('crm.prospecting.examAddQuestion') }}
        </button>
        <ul>
          <li v-for="question in questions" :key="question.basicId">
            {{ question.prompt }} [{{ question.category || t('crm.prospecting.examNoCategory') }}] ({{ question.points }})
          </li>
        </ul>
      </div>

      <div class="ce-block">
        <h4>{{ t('crm.prospecting.examPapers') }}</h4>
        <label>{{ t('crm.prospecting.examPaperName') }}<input v-model="newPaper.name" type="text"></label>
        <label>{{ t('crm.prospecting.examQuestionIds') }}<input v-model="newPaper.questionIds" type="text"></label>
        <label>{{ t('crm.prospecting.examSelectionRules') }}
          <input v-model="newPaper.selectionRules" type="text" :placeholder="t('crm.prospecting.examSelectionRulesHint')">
        </label>
        <label class="ce-check">
          <input v-model="newPaper.randomizeSelection" type="checkbox">
          {{ t('crm.prospecting.examRandomize') }}
        </label>
        <label v-if="newPaper.randomizeSelection">{{ t('crm.prospecting.examSeed') }}<input v-model="newPaper.selectionSeed" type="number"></label>
        <label>{{ t('crm.prospecting.examDuration') }}<input v-model.number="newPaper.durationMinutes" type="number" min="1" max="180"></label>
        <label>{{ t('crm.prospecting.examPassScore') }}<input v-model.number="newPaper.passScore" type="number" min="1"></label>
        <label>{{ t('crm.prospecting.examMaxAttempts') }}<input v-model.number="newPaper.maxAttempts" type="number" min="1" max="10"></label>
        <button type="button" :disabled="!newPaper.name" @click="addPaper">
          {{ t('crm.prospecting.examCreatePaper') }}
        </button>
        <ul>
          <li v-for="paper in papers" :key="paper.basicId">
            {{ paper.name }} · {{ paper.questionCount }} · {{ paper.totalScore }} · {{ statusLabel(paper.status) }}
            <small v-if="paper.selectionRules.length">{{ paper.selectionRules.map(rule => `${rule.category}:${rule.count}`).join(' ') }}{{ paper.randomizeSelection ? ` · ${t('crm.prospecting.examSeed')}:${paper.selectionSeed}` : '' }}</small>
            <button v-if="paper.status === 0" type="button" @click="publish(paper.basicId)">
              {{ t('crm.prospecting.examPublish') }}
            </button>
            <button v-if="paper.status === 1" type="button" @click="start(paper.basicId)">
              {{ t('crm.prospecting.examStart') }}
            </button>
          </li>
        </ul>
      </div>

      <div class="ce-block">
        <h4>{{ t('crm.prospecting.examTake') }}</h4>
        <template v-if="active">
          <p v-for="question in active.questions" :key="question.basicId" class="ce-question">
            {{ question.prompt }}
            <template v-if="question.type === 2">
              <label v-for="option in question.options" :key="option" class="ce-option">
                <input type="checkbox" :checked="isMultiSelected(question.basicId, option)" @change="toggleMulti(question.basicId, option, $event)">
                {{ option }}
              </label>
            </template>
            <select v-else v-model="answers[question.basicId]">
              <option value="">
                {{ t('crm.prospecting.examNoAnswer') }}
              </option>
              <option v-for="option in question.options" :key="option" :value="option">
                {{ option }}
              </option>
            </select>
          </p>
          <button type="button" :disabled="active.status !== 0" @click="submit">
            {{ t('crm.prospecting.examSubmit') }}
          </button>
          <p v-if="active.status !== 0" class="ce-result">
            {{ t('crm.prospecting.examResult') }}: {{ active.score }}/{{ active.totalScore }} · {{ statusLabel(active.status) }}
          </p>
        </template>
        <p v-else class="ce-muted">
          {{ t('crm.prospecting.examStartHint') }}
        </p>
        <ul>
          <li v-for="attempt in attempts" :key="attempt.basicId">
            {{ attempt.paperName }} · {{ attempt.score }}/{{ attempt.totalScore }} · {{ statusLabel(attempt.status) }}
          </li>
        </ul>
      </div>
    </div>
  </section>
</template>

<style scoped>
.crm-exam {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
}
.ce-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
  gap: 0.8rem;
}
.ce-block {
  display: grid;
  align-content: start;
  gap: 0.4rem;
  padding: 0.6rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
}
.ce-block label {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ce-block input,
.ce-block select {
  width: 100%;
  font: inherit;
}
.ce-block ul {
  margin: 0;
  padding-left: 1.1rem;
  font-size: 0.82rem;
}
.ce-question {
  display: grid;
  gap: 0.3rem;
  font-size: 0.85rem;
}
.ce-check {
  display: flex;
  align-items: center;
  gap: 0.3rem;
}
.ce-option {
  display: inline-flex;
  gap: 0.25rem;
  align-items: center;
}
.ce-result {
  font-weight: 600;
}
</style>
