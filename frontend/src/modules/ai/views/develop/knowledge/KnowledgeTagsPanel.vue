<script setup lang="ts">
import type {
  KnowledgeDuplicateReportDto,
  KnowledgeTagItemDto,
} from '../../../api/knowledge-tags.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from '~/composables'
import { knowledgeTagsApi } from '../../../api/knowledge-tags'

defineOptions({ name: 'KnowledgeTagsPanel' })

const { t } = useI18n()
const documentId = ref('')
const tagsInput = ref('')
const tags = ref<KnowledgeTagItemDto[]>([])
const duplicates = ref<KnowledgeDuplicateReportDto | null>(null)

async function reload() {
  try {
    const [tagList, dup] = await Promise.all([
      knowledgeTagsApi.tags(),
      knowledgeTagsApi.duplicates(),
    ])
    tags.value = tagList
    duplicates.value = dup
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.tagError'))
  }
}

async function saveTags() {
  if (!documentId.value)
    return
  try {
    await knowledgeTagsApi.setTags({
      documentId: documentId.value,
      tags: tagsInput.value.split(',').map(tag => tag.trim()).filter(Boolean),
    })
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.tagError'))
  }
}
</script>

<template>
  <section class="knowledge-tags">
    <header class="kt-head">
      <h3>{{ t('develop.knowledge.tagTitle') }}</h3>
      <p>{{ t('develop.knowledge.tagDescription') }}</p>
    </header>
    <div class="kt-form">
      <label>{{ t('develop.knowledge.tagDocumentId') }}<input v-model="documentId" type="text"></label>
      <label>{{ t('develop.knowledge.tagValues') }}<input v-model="tagsInput" type="text"></label>
      <button type="button" :disabled="!documentId" @click="saveTags">
        {{ t('develop.knowledge.tagSave') }}
      </button>
      <button type="button" @click="reload">
        {{ t('develop.knowledge.tagRefresh') }}
      </button>
    </div>
    <div class="kt-tags">
      <span v-for="item in tags" :key="item.tag">{{ item.tag }} ({{ item.documentCount }})</span>
      <span v-if="tags.length === 0" class="oc-muted">{{ t('develop.knowledge.tagEmpty') }}</span>
    </div>
    <div class="kt-dups">
      <strong>{{ t('develop.knowledge.dupTitle') }}</strong>
      <span>{{ t('develop.knowledge.dupSummary', { groups: duplicates?.groups ?? 0, documents: duplicates?.duplicateDocuments ?? 0 }) }}</span>
      <ul>
        <li v-for="item in duplicates?.items ?? []" :key="item.hash">
          {{ item.titles.join(' · ') }} ({{ item.documentIds.length }})
        </li>
      </ul>
    </div>
  </section>
</template>

<style scoped>
.knowledge-tags {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
}
.kt-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.5rem;
}
.kt-form label {
  display: grid;
  flex: 1 1 12rem;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.kt-form input {
  width: 100%;
  font: inherit;
}
.kt-tags,
.kt-dups {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  font-size: 0.82rem;
}
.kt-dups {
  display: grid;
  gap: 0.3rem;
}
</style>
