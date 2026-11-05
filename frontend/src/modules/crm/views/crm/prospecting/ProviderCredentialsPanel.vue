<script setup lang="ts">
import type { ConfigCreateDto, ConfigListItemDto, ConfigUpdateDto } from '@/api'
import { onMounted, ref } from 'vue'
import { ConfigDataType, ConfigType, configApi, createPageRequest, EnableStatus } from '@/api'
import { toast } from '~/composables'

defineOptions({ name: 'CrmProspectingProviderCredentials' })

type ProviderCredential = {
  code: string
  name: string
  description: string
  configKey: string
  configured: boolean
  value: string
  record?: ConfigListItemDto
}

const definitions = [
  { code: 'serper', name: 'Serper / Google Search', description: 'Google 搜索结果 API，按套餐/调用量计费' },
  { code: 'brave', name: 'Brave Search', description: 'Brave Search API Token' },
  { code: 'serpapi', name: 'SerpApi', description: 'Google 搜索 API Key，可能产生调用费用' },
  { code: 'google_places', name: 'Google Places', description: 'Google Maps Platform API Key，需启用计费与 Places API' },
  { code: 'sam_gov', name: 'SAM.gov', description: '美国联邦采购机会 API Key' },
  { code: 'un_comtrade', name: 'UN Comtrade', description: '贸易统计 API subscription key；公共预览接口可不填 Key' },
  { code: 'us_census', name: 'US Census Trade', description: '美国 Census API Key（免费申请）' },
  { code: 'apollo', name: 'Apollo.io', description: '商业联系人数据 API Key，需确认套餐与隐私条款' },
  { code: 'people_data_labs', name: 'People Data Labs', description: '企业/人员数据 API Key，需确认额度与隐私条款' },
  { code: 'hunter', name: 'Hunter.io', description: '域名邮箱发现 API Key；不会触发自动外联' },
  { code: 'open_corporates', name: 'OpenCorporates', description: '企业登记聚合 API Key / 许可凭证' },
  { code: 'kvk', name: 'Netherlands KVK', description: '荷兰商会企业登记 API Key / 订阅凭证' },
  { code: 'companies_house', name: 'UK Companies House', description: '英国公司注册处 API Key（HTTP Basic 用户名凭证）' },
] as const

const loading = ref(false)
const saving = ref(false)
const credentials = ref<ProviderCredential[]>([])

async function load() {
  loading.value = true
  try {
    const page = await configApi.page({
      ...createPageRequest({ page: { pageIndex: 1, pageSize: 200 } }),
      configGroup: 'CRM_PROSPECTING_PROVIDER',
      isEncrypted: true,
    })
    const rows = (page as unknown as { items?: ConfigListItemDto[]; data?: ConfigListItemDto[] }).items
      ?? (page as unknown as { data?: ConfigListItemDto[] }).data
      ?? []
    credentials.value = definitions.map(item => {
      const record = rows.find(row => row.configKey === `CRM.Prospecting.Provider.${item.code}.ApiKey`)
      return {
        ...item,
        configKey: `CRM.Prospecting.Provider.${item.code}.ApiKey`,
        configured: Boolean(record?.hasCurrentValue),
        value: '',
        record,
      }
    })
  }
  catch (error) {
    toast.danger((error as Error)?.message || '加载数据源凭证状态失败')
  }
  finally {
    loading.value = false
  }
}

async function save(item: ProviderCredential) {
  if (!item.value.trim()) {
    toast.warning('请先输入 Key；如需清除已保存的 Key，请使用“清除凭证”')
    return
  }
  saving.value = true
  try {
    if (item.record) {
      const input: ConfigUpdateDto = {
        basicId: item.record.basicId,
        configName: item.name,
        configGroup: 'CRM_PROSPECTING_PROVIDER',
        configType: ConfigType.Tenant,
        configValue: item.value.trim(),
        dataType: ConfigDataType.String,
        isEncrypted: true,
        sort: 500,
      }
      await configApi.update(input)
      if (item.record.status !== EnableStatus.Enabled) {
        await configApi.updateStatus({ basicId: item.record.basicId, status: EnableStatus.Enabled, remark: '启用获客数据源凭证配置值' })
      }
    }
    else {
      const input: ConfigCreateDto = {
        configKey: item.configKey,
        configName: item.name,
        configGroup: 'CRM_PROSPECTING_PROVIDER',
        configType: ConfigType.Tenant,
        configValue: item.value.trim(),
        dataType: ConfigDataType.String,
        isEncrypted: true,
        sort: 500,
        status: EnableStatus.Enabled,
      }
      await configApi.create(input)
    }
    item.value = ''
    toast.success(`${item.name} 凭证已加密保存；是否执行数据源仍由部署开关和获客策略控制。`)
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || '保存数据源凭证失败')
  }
  finally {
    saving.value = false
  }
}

async function clear(item: ProviderCredential) {
  if (!item.record) return
  saving.value = true
  try {
    const input: ConfigUpdateDto = {
      basicId: item.record.basicId,
      configName: item.name,
      configGroup: 'CRM_PROSPECTING_PROVIDER',
      configType: ConfigType.Tenant,
      configValue: '',
      dataType: ConfigDataType.String,
      isEncrypted: true,
      sort: 500,
    }
    await configApi.update(input)
    item.value = ''
    toast.success(`${item.name} 凭证已清除`)
    await load()
  }
  catch (error) {
    toast.danger((error as Error)?.message || '清除凭证失败')
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="panel provider-credentials">
    <header class="section-header">
      <div>
        <h2>数据源 API 凭证</h2>
        <p>统一录入需要 Key / Token 的数据源凭证。密钥加密保存，读取时只返回配置状态。</p>
      </div>
      <button type="button" :disabled="loading" @click="load">{{ loading ? '加载中…' : '刷新状态' }}</button>
    </header>
    <div class="credential-security-note">
      <strong>安全提示：</strong>保存后不会再次显示完整 Key；留空不会覆盖已有值。保存凭证不会自动启用数据源，也不会自动发起付费调用。
    </div>
    <div class="credential-grid">
      <article v-for="item in credentials" :key="item.code" class="credential-row">
        <div class="credential-info">
          <strong>{{ item.name }}</strong>
          <small>{{ item.description }}</small>
          <span class="credential-state" :class="{ configured: item.configured }">
            {{ item.configured ? '凭证已保存' : '尚未配置' }}
          </span>
        </div>
        <div class="credential-actions">
          <input
            v-model="item.value"
            type="password"
            autocomplete="new-password"
            spellcheck="false"
            :placeholder="item.configured ? '已配置；输入新 Key 可轮换' : '输入 API Key / Token'"
            :aria-label="`${item.name} API Key`"
          >
          <button type="button" :disabled="saving || !item.value.trim()" @click="save(item)">保存</button>
          <button v-if="item.configured" type="button" class="danger-button" :disabled="saving" @click="clear(item)">清除</button>
        </div>
      </article>
    </div>
  </section>
</template>

<style scoped>
.provider-credentials { margin-bottom: 1rem; }
.credential-security-note { margin: .75rem 0 1rem; padding: .75rem 1rem; border-radius: .5rem; background: var(--xh-color-bg-subtle, #f5f5f5); font-size: .875rem; }
.credential-grid { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); }
.credential-row { min-width: 0; padding: .85rem; border: 1px solid var(--xh-color-border, #ddd); border-radius: .5rem; }
.credential-info { display: grid; gap: .25rem; margin-bottom: .65rem; }
.credential-info small { color: var(--xh-color-text-muted, #777); }
.credential-state { font-size: .75rem; color: #a16207; }
.credential-state.configured { color: #15803d; }
.credential-actions { display: flex; flex-wrap: wrap; gap: .4rem; }
.credential-actions input { flex: 1 1 180px; min-width: 0; padding: .5rem .65rem; border: 1px solid var(--xh-color-border, #ccc); border-radius: .35rem; }
.credential-actions button { padding: .45rem .7rem; border: 1px solid var(--xh-color-border, #ccc); border-radius: .35rem; }
.credential-actions .danger-button { color: #b91c1c; }
</style>
