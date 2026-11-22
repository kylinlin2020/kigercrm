<script setup lang="ts">
import type { CrmCustomerPoolEntryDto } from '@/api/modules/crm/customer-pool.types'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmCustomerPoolApi } from '@/api/modules/crm/customer-pool'
import { toast } from '~/composables'

defineOptions({ name: 'CrmCustomerPoolPage' })

const { t } = useI18n()
const available = ref<CrmCustomerPoolEntryDto[]>([])
const mine = ref<CrmCustomerPoolEntryDto[]>([])
const customerId = ref('')
const assignUserId = ref('')

async function reload() {
  try {
    const [pool, claimed] = await Promise.all([
      crmCustomerPoolApi.available(),
      crmCustomerPoolApi.mine(),
    ])
    available.value = pool
    mine.value = claimed
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customerPool.error'))
  }
}

async function add() {
  if (!customerId.value)
    return
  try {
    await crmCustomerPoolApi.add({ customerId: customerId.value })
    customerId.value = ''
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customerPool.error'))
  }
}

async function act(fn: () => Promise<unknown>, successKey: string) {
  try {
    await fn()
    toast.success(t(successKey))
    await reload()
  }
  catch (error) {
    toast.danger((error as Error)?.message || t('crm.customerPool.error'))
  }
}

function statusLabel(status: number) {
  return [
    t('crm.customerPool.statusAvailable'),
    t('crm.customerPool.statusClaimed'),
    t('crm.customerPool.statusRecovered'),
  ][status] ?? String(status)
}
</script>

<template>
  <main class="customer-pool-page">
    <header class="page-header">
      <div>
        <h1>{{ t('crm.customerPool.title') }}</h1>
        <p>{{ t('crm.customerPool.description') }}</p>
      </div>
      <button type="button" @click="reload">
        {{ t('crm.customerPool.refresh') }}
      </button>
    </header>

    <section class="pool-form">
      <label>{{ t('crm.customerPool.customerId') }}
        <input v-model="customerId" type="text">
      </label>
      <button type="button" :disabled="!customerId" @click="add">
        {{ t('crm.customerPool.add') }}
      </button>
      <label>{{ t('crm.customerPool.assignUserId') }}
        <input v-model="assignUserId" type="text">
      </label>
      <button type="button" @click="act(() => crmCustomerPoolApi.recoverIdle(), 'crm.customerPool.recovered')">
        {{ t('crm.customerPool.recoverIdle') }}
      </button>
    </section>

    <section class="pool-section">
      <h2>{{ t('crm.customerPool.availableTitle') }}</h2>
      <p v-if="available.length === 0">
        {{ t('crm.customerPool.noAvailable') }}
      </p>
      <div v-for="entry in available" :key="entry.basicId" class="pool-row">
        <span>{{ entry.customerName }} · {{ statusLabel(entry.status) }} · {{ entry.claimCount }}</span>
        <button type="button" @click="act(() => crmCustomerPoolApi.claim({ entryId: entry.basicId }), 'crm.customerPool.claimed')">
          {{ t('crm.customerPool.claim') }}
        </button>
        <button type="button" :disabled="!assignUserId" @click="act(() => crmCustomerPoolApi.assign(entry.basicId, assignUserId), 'crm.customerPool.assigned')">
          {{ t('crm.customerPool.assign') }}
        </button>
      </div>
    </section>

    <section class="pool-section">
      <h2>{{ t('crm.customerPool.mineTitle') }}</h2>
      <p v-if="mine.length === 0">
        {{ t('crm.customerPool.noMine') }}
      </p>
      <div v-for="entry in mine" :key="entry.basicId" class="pool-row">
        <span>{{ entry.customerName }} · {{ statusLabel(entry.status) }}</span>
        <button type="button" @click="act(() => crmCustomerPoolApi.release({ entryId: entry.basicId }), 'crm.customerPool.released')">
          {{ t('crm.customerPool.release') }}
        </button>
      </div>
    </section>
  </main>
</template>

<style scoped>
.customer-pool-page {
  display: grid;
  gap: 1rem;
  padding: 1.25rem;
}
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}
.page-header h1 {
  margin: 0;
  font-size: 1.35rem;
}
.page-header p,
.pool-section p {
  margin: 0.25rem 0 0;
  color: var(--xh-text-muted, #777);
}
.pool-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
}
.pool-form label {
  display: grid;
  gap: 0.2rem;
  font-size: 0.82rem;
}
.pool-form input {
  font: inherit;
}
.pool-section {
  display: grid;
  gap: 0.4rem;
}
.pool-section h2 {
  margin: 0;
  font-size: 1rem;
}
.pool-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-top: 1px solid var(--xh-border-subtle, #ddd);
  font-size: 0.85rem;
}
</style>
