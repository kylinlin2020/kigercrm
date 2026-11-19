<script setup lang="ts">
import type { KnowledgeReviewDto } from '../../../api/knowledge-review.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from '~/composables'
import { knowledgeReviewApi } from '../../../api/knowledge-review'

defineOptions({ name: 'KnowledgeReviewPanel' })

const { t } = useI18n()
const documentId = ref('')
const comment = ref('')
const reviews = ref<KnowledgeReviewDto[]>([])

function statusLabel(status: number) {
  return [
    t('develop.knowledge.reviewPending'),
    t('develop.knowledge.reviewApproved'),
    t('develop.knowledge.reviewRejected'),
  ][status] ?? String(status)
}

async function reload() {
  if (!documentId.value) {
    reviews.value = []
    return
  }
  try {
    reviews.value = await knowledgeReviewApi.reviews(documentId.value)
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.reviewError'))
  }
}

async function submit() {
  if (!documentId.value) {
    return
  }
  try {
    await knowledgeReviewApi.submit(documentId.value)
    toast.success(t('develop.knowledge.reviewSubmitted'))
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.reviewError'))
  }
}

async function decide(approve: boolean, reviewId: string) {
  try {
    await knowledgeReviewApi.decide({ reviewId, approve, comment: comment.value || null })
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.reviewError'))
  }
}
</script>

<template>
  <section class="knowledge-review">
    <header class="section-header">
      <div>
        <h3>{{ t('develop.knowledge.reviewTitle') }}</h3>
        <p>{{ t('develop.knowledge.reviewDescription') }}</p>
      </div>
    </header>
    <div class="kr-form">
      <label>{{ t('develop.knowledge.reviewDocumentId') }}
        <input v-model="documentId" type="text" @change="reload">
      </label>
      <label>{{ t('develop.knowledge.reviewComment') }}
        <input v-model="comment" type="text" maxlength="1000">
      </label>
      <button type="button" :disabled="!documentId" @click="submit">
        {{ t('develop.knowledge.reviewSubmit') }}
      </button>
    </div>
    <div v-for="review in reviews" :key="review.basicId" class="kr-row">
      <strong>{{ statusLabel(review.status) }}</strong>
      <small>{{ review.versionHash.slice(0, 12) }} · {{ review.comment || '—' }}</small>
      <template v-if="review.status === 0">
        <button type="button" @click="decide(true, review.basicId)">
          {{ t('develop.knowledge.reviewApproveAction') }}
        </button>
        <button type="button" @click="decide(false, review.basicId)">
          {{ t('develop.knowledge.reviewRejectAction') }}
        </button>
      </template>
    </div>
  </section>
</template>

<style scoped>
.knowledge-review {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.kr-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.kr-form label {
  display: grid;
  flex: 1 1 12rem;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.kr-form input {
  width: 100%;
  font: inherit;
}
.kr-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
.kr-row small {
  color: var(--xh-fg-muted, #777);
}
</style>
