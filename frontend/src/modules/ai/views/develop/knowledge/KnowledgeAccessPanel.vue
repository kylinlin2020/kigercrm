<script setup lang="ts">
import type { KnowledgeAccessDto } from '../../../api/knowledge-access.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from '~/composables'
import { knowledgeAccessApi } from '../../../api/knowledge-access'

defineOptions({ name: 'KnowledgeAccessPanel' })

const { t } = useI18n()
const documentId = ref('')
const visibility = ref(0)
const roles = ref('')
const access = ref<KnowledgeAccessDto | null>(null)

async function load() {
  if (!documentId.value)
    return
  try {
    access.value = await knowledgeAccessApi.get(documentId.value)
    visibility.value = access.value.visibility
    roles.value = access.value.allowedRoles.join(', ')
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.accessError'))
  }
}

async function save() {
  if (!documentId.value)
    return
  try {
    access.value = await knowledgeAccessApi.update({
      documentId: documentId.value,
      visibility: visibility.value,
      allowedRoles: visibility.value === 1
        ? roles.value.split(',').map(role => role.trim()).filter(Boolean)
        : [],
    })
    toast.success(t('develop.knowledge.accessSaved'))
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('develop.knowledge.accessError'))
  }
}
</script>

<template>
  <section class="knowledge-access">
    <header class="ka-head">
      <h3>{{ t('develop.knowledge.accessTitle') }}</h3>
      <p>{{ t('develop.knowledge.accessDescription') }}</p>
    </header>
    <div class="ka-form">
      <label>{{ t('develop.knowledge.accessDocId') }}
        <input v-model="documentId" type="text" @change="load">
      </label>
      <label>{{ t('develop.knowledge.accessVisibility') }}
        <select v-model.number="visibility">
          <option :value="0">{{ t('develop.knowledge.accessPublic') }}</option>
          <option :value="1">{{ t('develop.knowledge.accessRestricted') }}</option>
        </select>
      </label>
      <label v-if="visibility === 1">{{ t('develop.knowledge.accessRoles') }}
        <input v-model="roles" type="text" :placeholder="t('develop.knowledge.accessRolesHint')">
      </label>
      <button type="button" :disabled="!documentId" @click="save">
        {{ t('develop.knowledge.accessSave') }}
      </button>
    </div>
    <p v-if="access" class="ka-state">
      {{ t('develop.knowledge.accessState', { visibility: String(access.visibility), roles: access.allowedRoles.join(', ') || '—' }) }}
    </p>
  </section>
</template>

<style scoped>
.knowledge-access {
  display: grid;
  gap: 0.6rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
}
.ka-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.ka-form label {
  display: grid;
  flex: 1 1 12rem;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.ka-form input,
.ka-form select {
  width: 100%;
  font: inherit;
}
.ka-state {
  font-size: 0.82rem;
}
</style>
