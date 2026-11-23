<script setup lang="ts">
import type {
  KnowledgeDuplicateReportDto,
  KnowledgeTagItemDto,
} from '../../../api/knowledge-tags.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput } from '~/components'
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
      <XhFieldRoot class="kt-field">
        <XhFieldLabel>{{ t('develop.knowledge.tagDocumentId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="documentId" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="kt-field">
        <XhFieldLabel>{{ t('develop.knowledge.tagValues') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="tagsInput" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!documentId" @click="saveTags">
        <XhButtonLabel>{{ t('develop.knowledge.tagSave') }}</XhButtonLabel>
      </XhButton>
      <XhButton variant="subtle" @click="reload">
        <XhButtonLabel>{{ t('develop.knowledge.tagRefresh') }}</XhButtonLabel>
      </XhButton>
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
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
}
.kt-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.5rem;
}
.kt-field {
  flex: 1 1 12rem;
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
