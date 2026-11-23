<script setup lang="ts">
import type { KnowledgeReviewDto } from '../../../api/knowledge-review.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput } from '~/components'
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
      <XhFieldRoot class="kr-field">
        <XhFieldLabel>{{ t('develop.knowledge.reviewDocumentId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="documentId" @change="reload" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="kr-field">
        <XhFieldLabel>{{ t('develop.knowledge.reviewComment') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="comment" :max-length="1000" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!documentId" @click="submit">
        <XhButtonLabel>{{ t('develop.knowledge.reviewSubmit') }}</XhButtonLabel>
      </XhButton>
    </div>
    <div v-for="review in reviews" :key="review.basicId" class="kr-row">
      <strong>{{ statusLabel(review.status) }}</strong>
      <small>{{ review.versionHash.slice(0, 12) }} · {{ review.comment || '—' }}</small>
      <template v-if="review.status === 0">
        <XhButton variant="outline" @click="decide(true, review.basicId)">
          <XhButtonLabel>{{ t('develop.knowledge.reviewApproveAction') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" tone="danger" @click="decide(false, review.basicId)">
          <XhButtonLabel>{{ t('develop.knowledge.reviewRejectAction') }}</XhButtonLabel>
        </XhButton>
      </template>
    </div>
  </section>
</template>

<style scoped>
.knowledge-review {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  background: hsl(var(--muted));
}
.kr-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.kr-field {
  flex: 1 1 12rem;
}
.kr-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
.kr-row small {
  color: hsl(var(--muted-foreground));
}
</style>
