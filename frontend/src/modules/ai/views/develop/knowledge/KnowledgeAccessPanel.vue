<script setup lang="ts">
import type { KnowledgeAccessDto } from '../../../api/knowledge-access.types'
import { XhButton, XhButtonLabel, XhFieldControl, XhFieldLabel, XhFieldRoot } from '@xihan-ui/vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { XInput, XSelect } from '~/components'
import { toast } from '~/composables'
import { knowledgeAccessApi } from '../../../api/knowledge-access'

defineOptions({ name: 'KnowledgeAccessPanel' })

const { t } = useI18n()
const documentId = ref('')
const visibility = ref(0)
const roles = ref('')
const access = ref<KnowledgeAccessDto | null>(null)

const visibilityOptions = computed(() => [
  { label: t('develop.knowledge.accessPublic'), value: 0 },
  { label: t('develop.knowledge.accessRestricted'), value: 1 },
])

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
      <XhFieldRoot class="ka-field">
        <XhFieldLabel>{{ t('develop.knowledge.accessDocId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="documentId" @change="load" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot class="ka-field">
        <XhFieldLabel>{{ t('develop.knowledge.accessVisibility') }}</XhFieldLabel>
        <XhFieldControl>
          <XSelect v-model:value="visibility" :options="visibilityOptions" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhFieldRoot v-if="visibility === 1" class="ka-field">
        <XhFieldLabel>{{ t('develop.knowledge.accessRoles') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="roles" :placeholder="t('develop.knowledge.accessRolesHint')" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!documentId" @click="save">
        <XhButtonLabel>{{ t('develop.knowledge.accessSave') }}</XhButtonLabel>
      </XhButton>
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
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
}
.ka-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.ka-field {
  flex: 1 1 12rem;
}
.ka-state {
  font-size: 0.82rem;
}
</style>
