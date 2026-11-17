<script setup lang="ts">
import type { CrmProspectingSuperSearchPreviewDto } from '@/api'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  crmProspectingApi,
  CrmProspectingSuperSearchDepth,
} from '@/api'
import { toast } from '~/composables'
import { usePermission } from '~/hooks'

defineOptions({ name: 'CrmProspectingSuperSearchPreviewPanel' })

const { t } = useI18n()
const { hasPermission } = usePermission()
const canExecute = computed(() => hasPermission('crm_prospecting.execute'))

const products = ref('')
const markets = ref('')
const customerTypes = ref('')
const industries = ref('')
const providerCodes = ref('')
const excludedTerms = ref('')
const excludedDomains = ref('')
const depth = ref(CrmProspectingSuperSearchDepth.Balanced)
const rounds = ref('')

const preview = ref<CrmProspectingSuperSearchPreviewDto | null>(null)
const submitting = ref(false)

const maxRounds = computed(() => [4, 8, 16][Number(depth.value)] ?? 4)

function splitList(value: string): string[] {
  return value.split(/[\n,，;；]+/).map(item => item.trim()).filter(Boolean)
}

async function submitPreview() {
  if (!canExecute.value || submitting.value) {
    return
  }
  submitting.value = true
  try {
    preview.value = await crmProspectingApi.previewSuperSearch({
      products: splitList(products.value),
      markets: splitList(markets.value),
      customerTypes: splitList(customerTypes.value),
      industries: splitList(industries.value),
      providerCodes: splitList(providerCodes.value),
      excludedTerms: splitList(excludedTerms.value),
      excludedDomains: splitList(excludedDomains.value),
      depth: Number(depth.value) as CrmProspectingSuperSearchDepth,
      requestedRounds: rounds.value === '' ? null : Number(rounds.value),
    })
  }
  catch (error) {
    // A rejected preview must never leave a stale plan on screen.
    preview.value = null
    toast.danger((error as Error)?.message || t('crm.prospecting.superSearchError'))
  }
  finally {
    submitting.value = false
  }
}

function fingerprintShort(value: string) {
  return value.length > 12 ? `${value.slice(0, 12)}…` : value
}
</script>

<template>
  <section class="super-search">
    <header class="section-header">
      <div>
        <h2>{{ t('crm.prospecting.superSearchTitle') }}</h2>
        <p>{{ t('crm.prospecting.superSearchDescription') }}</p>
      </div>
      <button
        v-if="canExecute"
        class="primary-button"
        type="button"
        :disabled="submitting"
        @click="submitPreview"
      >
        {{ submitting ? t('crm.prospecting.superSearchSubmitting') : t('crm.prospecting.superSearchPreviewButton') }}
      </button>
    </header>

    <div class="search-form">
      <label>{{ t('crm.prospecting.superSearchProducts') }}
        <textarea v-model="products" rows="2" placeholder="industrial pump, valve" />
      </label>
      <label>{{ t('crm.prospecting.superSearchMarkets') }}
        <textarea v-model="markets" rows="2" placeholder="DE, BR" />
      </label>
      <label>{{ t('crm.prospecting.superSearchCustomerTypes') }}
        <textarea v-model="customerTypes" rows="2" placeholder="distributor, importer" />
      </label>
      <label>{{ t('crm.prospecting.superSearchIndustries') }}
        <textarea v-model="industries" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchProviders') }}
        <textarea v-model="providerCodes" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchExcludedTerms') }}
        <textarea v-model="excludedTerms" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchExcludedDomains') }}
        <textarea v-model="excludedDomains" rows="2" />
      </label>
      <label>{{ t('crm.prospecting.superSearchDepth') }}
        <select v-model.number="depth">
          <option :value="CrmProspectingSuperSearchDepth.Balanced">Balanced</option>
          <option :value="CrmProspectingSuperSearchDepth.Deep">Deep</option>
          <option :value="CrmProspectingSuperSearchDepth.Extreme">Extreme</option>
        </select>
      </label>
      <label>{{ t('crm.prospecting.superSearchRounds') }}
        <input v-model="rounds" type="number" min="1" :max="maxRounds" :placeholder="`1–${maxRounds}`">
      </label>
    </div>

    <p class="read-only-notice">
      {{ t('crm.prospecting.superSearchReadOnlyNotice') }}
    </p>

    <div role="status" aria-live="polite">
      <template v-if="preview">
        <div class="plan-summary">
          <span>{{ t('crm.prospecting.superSearchSummary', {
            rounds: preview.plannedRounds,
            maximum: preview.maximumRounds,
            cells: preview.totalQueryCells,
            providers: preview.providerCount,
          }) }}</span>
          <code :title="preview.scopeFingerprint">{{ fingerprintShort(preview.scopeFingerprint) }}</code>
        </div>
        <p v-if="preview.rounds.length === 0" class="empty-state">
          {{ t('crm.prospecting.superSearchNoRounds') }}
        </p>
        <details v-for="round in preview.rounds" :key="round.fingerprint" class="plan-round">
          <summary>
            {{ t('crm.prospecting.superSearchRound', { number: round.roundNumber }) }}
            · {{ round.themeLabel }} · {{ round.market }} / {{ round.customerType }}
          </summary>
          <div class="round-meta">
            <span>{{ t('crm.prospecting.superSearchExcludedTerms') }}: {{ round.excludedTerms.join(', ') || '—' }}</span>
            <span>{{ t('crm.prospecting.superSearchExcludedDomains') }}: {{ round.excludedDomains.join(', ') || '—' }}</span>
            <code :title="round.fingerprint">{{ fingerprintShort(round.fingerprint) }}</code>
          </div>
          <table class="cell-table">
            <thead>
              <tr>
                <th>{{ t('crm.prospecting.provider') }}</th>
                <th>{{ t('crm.prospecting.superSearchTheme') }}</th>
                <th>{{ t('crm.prospecting.superSearchQueryCells') }}</th>
                <th>{{ t('crm.prospecting.superSearchFingerprint') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="cell in round.queryCells" :key="cell.fingerprint">
                <td><code>{{ cell.providerCode }}</code></td>
                <td><code>{{ cell.themeCode }}</code></td>
                <td class="query-cell">
                  {{ cell.queryText }}
                </td>
                <td><code :title="cell.fingerprint">{{ fingerprintShort(cell.fingerprint) }}</code></td>
              </tr>
            </tbody>
          </table>
        </details>
      </template>
    </div>
  </section>
</template>

<style scoped>
.super-search {
  display: grid;
  gap: 0.75rem;
  padding: 0.85rem;
  border: 1px solid var(--xh-border-default, #ddd);
  border-radius: 0.5rem;
  background: var(--xh-bg-subtle, #f5f5f5);
}
.search-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.5rem 0.75rem;
}
.search-form label {
  display: grid;
  gap: 0.25rem;
  font-size: 0.85rem;
}
.search-form textarea,
.search-form input,
.search-form select {
  width: 100%;
  font: inherit;
}
.read-only-notice {
  margin: 0;
  color: var(--xh-fg-muted, #777);
  font-size: 0.8rem;
}
.plan-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.plan-round {
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  padding: 0.5rem 0;
}
.plan-round summary {
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
}
.round-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin: 0.4rem 0;
  color: var(--xh-fg-muted, #777);
  font-size: 0.8rem;
}
.cell-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}
.cell-table th,
.cell-table td {
  padding: 0.4rem 0.5rem;
  border-bottom: 1px solid var(--xh-border-subtle, #ddd);
  text-align: left;
  vertical-align: top;
}
.query-cell {
  max-width: 24rem;
  overflow-wrap: anywhere;
}
.empty-state {
  margin: 0;
  color: var(--xh-fg-muted, #777);
}
</style>
