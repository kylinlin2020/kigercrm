<script setup lang="ts">
import type { CrmExamAttemptDto, CrmExamPaperDto, CrmExamQuestionDto } from '@/api/modules/crm/exam.types'
import {
  XhButton,
  XhButtonLabel,
  XhCheckbox,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmExamApi } from '@/api/modules/crm/exam'
import {

  CrmExamQuestionType,
} from '@/api/modules/crm/exam.types'
import { XInput, XSelect } from '~/components'
import { toast } from '~/composables'

defineOptions({ name: 'CrmExamPanel' })

const { t } = useI18n()

const questions = ref<CrmExamQuestionDto[]>([])
const papers = ref<CrmExamPaperDto[]>([])
const attempts = ref<CrmExamAttemptDto[]>([])
const active = ref<CrmExamAttemptDto | null>(null)
const answers = ref<Record<string, string>>({})

const newQuestion = ref({ prompt: '', type: CrmExamQuestionType.SingleChoice, category: '', options: 'A, B', correctAnswer: 'A', correctAnswers: 'A, B', points: '1' })
const newPaper = ref({ name: '', questionIds: '', selectionRules: '', randomizeSelection: false, selectionSeed: '42', durationMinutes: '30', passScore: '60', maxAttempts: '3' })

const questionTypeOptions = computed(() => [
  { label: t('crm.prospecting.examTypeSingle'), value: 0 },
  { label: t('crm.prospecting.examTypeTrueFalse'), value: 1 },
  { label: t('crm.prospecting.examTypeMulti'), value: 2 },
])

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

function toggleMulti(questionId: string, option: string, checked: boolean) {
  const selected = new Set((answers.value[questionId] ?? '').split(',').filter(Boolean))
  if (checked)
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
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examPrompt') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.prompt" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examCategory') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.category" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examType') }}</XhFieldLabel>
          <XhFieldControl>
            <XSelect v-model:value="newQuestion.type" :options="questionTypeOptions" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examOptions') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.options" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot v-if="newQuestion.type === 2">
          <XhFieldLabel>{{ t('crm.prospecting.examCorrectMulti') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.correctAnswers" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot v-else>
          <XhFieldLabel>{{ t('crm.prospecting.examCorrect') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.correctAnswer" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examPoints') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newQuestion.points" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="outline" :disabled="!newQuestion.prompt" @click="addQuestion">
          <XhButtonLabel>{{ t('crm.prospecting.examAddQuestion') }}</XhButtonLabel>
        </XhButton>
        <ul>
          <li v-for="question in questions" :key="question.basicId">
            {{ question.prompt }} [{{ question.category || t('crm.prospecting.examNoCategory') }}] ({{ question.points }})
          </li>
        </ul>
      </div>

      <div class="ce-block">
        <h4>{{ t('crm.prospecting.examPapers') }}</h4>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examPaperName') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.name" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examQuestionIds') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.questionIds" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examSelectionRules') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.selectionRules" :placeholder="t('crm.prospecting.examSelectionRulesHint')" />
          </XhFieldControl>
        </XhFieldRoot>
        <div class="ce-check">
          <XhCheckbox v-model:checked="newPaper.randomizeSelection" :aria-label="t('crm.prospecting.examRandomize')" />
          <span>{{ t('crm.prospecting.examRandomize') }}</span>
        </div>
        <XhFieldRoot v-if="newPaper.randomizeSelection">
          <XhFieldLabel>{{ t('crm.prospecting.examSeed') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.selectionSeed" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examDuration') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.durationMinutes" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examPassScore') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.passScore" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhFieldRoot>
          <XhFieldLabel>{{ t('crm.prospecting.examMaxAttempts') }}</XhFieldLabel>
          <XhFieldControl>
            <XInput v-model:value="newPaper.maxAttempts" inputmode="numeric" />
          </XhFieldControl>
        </XhFieldRoot>
        <XhButton variant="outline" :disabled="!newPaper.name" @click="addPaper">
          <XhButtonLabel>{{ t('crm.prospecting.examCreatePaper') }}</XhButtonLabel>
        </XhButton>
        <ul>
          <li v-for="paper in papers" :key="paper.basicId">
            {{ paper.name }} · {{ paper.questionCount }} · {{ paper.totalScore }} · {{ statusLabel(paper.status) }}
            <small v-if="paper.selectionRules.length">{{ paper.selectionRules.map(rule => `${rule.category}:${rule.count}`).join(' ') }}{{ paper.randomizeSelection ? ` · ${t('crm.prospecting.examSeed')}:${paper.selectionSeed}` : '' }}</small>
            <XhButton v-if="paper.status === 0" variant="outline" @click="publish(paper.basicId)">
              <XhButtonLabel>{{ t('crm.prospecting.examPublish') }}</XhButtonLabel>
            </XhButton>
            <XhButton v-if="paper.status === 1" variant="outline" @click="start(paper.basicId)">
              <XhButtonLabel>{{ t('crm.prospecting.examStart') }}</XhButtonLabel>
            </XhButton>
          </li>
        </ul>
      </div>

      <div class="ce-block">
        <h4>{{ t('crm.prospecting.examTake') }}</h4>
        <template v-if="active">
          <div v-for="question in active.questions" :key="question.basicId" class="ce-question">
            {{ question.prompt }}
            <template v-if="question.type === 2">
              <XhCheckbox
                v-for="option in question.options"
                :key="option"
                class="ce-option"
                :checked="isMultiSelected(question.basicId, option)"
                :aria-label="option"
                @update:checked="(checked) => toggleMulti(question.basicId, option, checked)"
              >
                {{ option }}
              </XhCheckbox>
            </template>
            <XSelect
              v-else
              v-model:value="answers[question.basicId]"
              :options="[
                { label: t('crm.prospecting.examNoAnswer'), value: '' },
                ...question.options.map(option => ({ label: option, value: option })),
              ]"
            />
          </div>
          <XhButton variant="outline" :disabled="active.status !== 0" @click="submit">
            <XhButtonLabel>{{ t('crm.prospecting.examSubmit') }}</XhButtonLabel>
          </XhButton>
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
  border: 1px solid hsl(var(--border));
  border-radius: var(--xh-radius-md);
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
