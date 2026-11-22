<script setup lang="ts">
import type { CrmCustomerPoolEntryDto } from '@/api/modules/crm/customer-pool.types'
import {
  XhButton,
  XhButtonLabel,
  XhFieldControl,
  XhFieldLabel,
  XhFieldRoot,
} from '@xihan-ui/vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { crmCustomerPoolApi } from '@/api/modules/crm/customer-pool'
import { XInput } from '~/components'
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
      <XhButton variant="subtle" @click="reload">
        <XhButtonLabel>{{ t('crm.customerPool.refresh') }}</XhButtonLabel>
      </XhButton>
    </header>

    <section class="pool-form">
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.customerPool.customerId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="customerId" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" :disabled="!customerId" @click="add">
        <XhButtonLabel>{{ t('crm.customerPool.add') }}</XhButtonLabel>
      </XhButton>
      <XhFieldRoot>
        <XhFieldLabel>{{ t('crm.customerPool.assignUserId') }}</XhFieldLabel>
        <XhFieldControl>
          <XInput v-model:value="assignUserId" />
        </XhFieldControl>
      </XhFieldRoot>
      <XhButton variant="outline" @click="act(() => crmCustomerPoolApi.recoverIdle(), 'crm.customerPool.recovered')">
        <XhButtonLabel>{{ t('crm.customerPool.recoverIdle') }}</XhButtonLabel>
      </XhButton>
    </section>

    <section class="pool-section">
      <h2>{{ t('crm.customerPool.availableTitle') }}</h2>
      <p v-if="available.length === 0">
        {{ t('crm.customerPool.noAvailable') }}
      </p>
      <div v-for="entry in available" :key="entry.basicId" class="pool-row">
        <span>{{ entry.customerName }} · {{ statusLabel(entry.status) }} · {{ entry.claimCount }}</span>
        <XhButton variant="outline" @click="act(() => crmCustomerPoolApi.claim({ entryId: entry.basicId }), 'crm.customerPool.claimed')">
          <XhButtonLabel>{{ t('crm.customerPool.claim') }}</XhButtonLabel>
        </XhButton>
        <XhButton variant="subtle" :disabled="!assignUserId" @click="act(() => crmCustomerPoolApi.assign(entry.basicId, assignUserId), 'crm.customerPool.assigned')">
          <XhButtonLabel>{{ t('crm.customerPool.assign') }}</XhButtonLabel>
        </XhButton>
      </div>
    </section>

    <section class="pool-section">
      <h2>{{ t('crm.customerPool.mineTitle') }}</h2>
      <p v-if="mine.length === 0">
        {{ t('crm.customerPool.noMine') }}
      </p>
      <div v-for="entry in mine" :key="entry.basicId" class="pool-row">
        <span>{{ entry.customerName }} · {{ statusLabel(entry.status) }}</span>
        <XhButton variant="subtle" @click="act(() => crmCustomerPoolApi.release({ entryId: entry.basicId }), 'crm.customerPool.released')">
          <XhButtonLabel>{{ t('crm.customerPool.release') }}</XhButtonLabel>
        </XhButton>
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
  color: hsl(var(--muted-foreground));
}
.pool-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.6rem;
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
  border-top: 1px solid hsl(var(--border));
  font-size: 0.85rem;
}
</style>
