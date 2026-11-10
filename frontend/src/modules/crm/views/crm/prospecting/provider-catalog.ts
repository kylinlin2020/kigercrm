/**
 * CRM 自动获客「数据源目录」的前端兜底清单。
 *
 * 唯一事实源是后端 `CrmProspectingProviderCatalogQueryService`（GET /api/CrmProspectingProviderCatalogQuery/Providers），
 * 它把静态元数据与 DI 中真实注册的 Provider 合并后返回 `isRegistered/isExecutable/capability`。
 * 本文件只在目录 API 不可用（未登录、无权限、后端旧版本、网络失败）时提供可渲染的降级数据，
 * 此时所有条目的启用状态一律标记为 unknown，界面必须明确告知用户，避免误报「已启用」。
 *
 * 证据等级 A：code 与后端 Infrastructure/Prospecting 各适配器 ProviderCode 一一对应。
 */
import type { CrmProspectingProviderCatalogItemDto } from '@/api'

export type CrmProspectingProviderGroup = 'search' | 'registry' | 'procurement' | 'trade' | 'enrichment'
export type CrmProspectingProviderDefinition = CrmProspectingProviderCatalogItemDto

export const CRM_PROSPECTING_PROVIDER_GROUPS: { key: CrmProspectingProviderGroup, label: string }[] = [
  { key: 'search', label: '搜索发现' },
  { key: 'registry', label: '官方登记 / 身份' },
  { key: 'procurement', label: '公共采购 / 招标' },
  { key: 'trade', label: '国际贸易统计' },
  { key: 'enrichment', label: '企业与区域名录' },
]

interface FallbackDefinition {
  code: string
  label: string
  group: CrmProspectingProviderGroup
  needsKey: boolean
  keyOptional?: boolean
  note: string
}

const FALLBACK_DEFINITIONS: FallbackDefinition[] = [
  { code: 'serper', label: 'Serper Google 搜索', group: 'search', needsKey: true, note: '按 credits 计费，结果为网页发现线索' },
  { code: 'brave', label: 'Brave Web Search', group: 'search', needsKey: true, note: '需订阅 Token，最多 2 页' },
  { code: 'serpapi', label: 'SerpApi Google 搜索', group: 'search', needsKey: true, note: '按套餐计费，最多 2 页' },

  { code: 'companies_house', label: '英国 Companies House', group: 'registry', needsKey: true, note: '英国官方公司登记，HTTP Basic 凭证' },
  { code: 'kvk', label: '荷兰 KVK', group: 'registry', needsKey: true, note: '荷兰官方企业登记，需订阅 Key' },
  { code: 'fr_company_search', label: '法国政府企业目录', group: 'registry', needsKey: false, note: '免 Key，仅法国市场，SIREN/SIRET 身份线索' },
  { code: 'sec_edgar', label: '美国 SEC EDGAR', group: 'registry', needsKey: true, note: '非 API Key，需填写系统名 + 联系邮箱作为 User-Agent' },
  { code: 'ror', label: 'ROR 研究机构标识', group: 'registry', needsKey: false, note: '免 Key，研究机构身份，不作普通公司库' },
  { code: 'wikidata', label: 'Wikidata 组织实体', group: 'registry', needsKey: false, note: '免 Key，知识图谱描述线索，非企业注册' },
  { code: 'nppes', label: '美国 NPPES 组织名录', group: 'registry', needsKey: false, note: '免 Key，仅美国医疗行业、NPI-2 组织记录' },
  { code: 'open_fda_510k', label: 'openFDA 510(k)', group: 'registry', needsKey: true, keyOptional: true, note: 'Key 可选；仅美国医疗器械活动，为监管证据而非采购意向' },

  { code: 'eu_ted', label: '欧盟 TED 招标公告', group: 'procurement', needsKey: false, note: '免 Key，欧盟官方招标公告' },
  { code: 'uk_find_tender', label: '英国 Find a Tender', group: 'procurement', needsKey: false, note: '免 Key，英国官方招标公告' },
  { code: 'uk_contracts_finder', label: '英国 Contracts Finder', group: 'procurement', needsKey: false, note: '免 Key，英国公共采购公告' },
  { code: 'usaspending_awards', label: '美国 USAspending', group: 'procurement', needsKey: false, note: '免 Key，美国联邦合同 / 支出记录' },
  { code: 'world_bank_procurement', label: '世界银行采购公告', group: 'procurement', needsKey: false, note: '免 Key，单页上限 20 条' },
  { code: 'brazil_pncp', label: '巴西 PNCP', group: 'procurement', needsKey: false, note: '免 Key，巴西国家公共采购门户' },
  { code: 'singapore_gebiz', label: '新加坡 GeBIZ', group: 'procurement', needsKey: false, note: '免 Key，历史授标记录而非开放招标' },
  { code: 'sam_gov', label: '美国 SAM.gov 机会', group: 'procurement', needsKey: true, note: '需官方 API Key，最近 30 天窗口、最多 3 页' },

  { code: 'un_comtrade', label: 'UN Comtrade', group: 'trade', needsKey: true, keyOptional: true, note: 'Key 可选；结果为聚合贸易信号，不是具体进口商' },
  { code: 'us_census_trade', label: '美国 Census 贸易统计', group: 'trade', needsKey: true, note: '需 Census API Key，HS2 出口统计信号' },

  { code: 'hunter', label: 'Hunter Discover', group: 'enrichment', needsKey: true, note: '仅发现企业域名与邮箱数量，不拉取联系人邮箱' },
  { code: 'mexico_denue', label: '墨西哥 INEGI DENUE', group: 'enrichment', needsKey: true, note: '路径 Token，全链路脱敏验证未完成前默认不注册' },
  { code: 'fmcsa_qcmobile', label: '美国 FMCSA QCMobile', group: 'enrichment', needsKey: true, note: '仅承运商目录，WebKey 查询参数需脱敏验证' },
]

export const CRM_PROSPECTING_PROVIDER_FALLBACK_CATALOG: CrmProspectingProviderCatalogItemDto[] = FALLBACK_DEFINITIONS.map(item => ({
  ...item,
  keyOptional: item.keyOptional ?? false,
  configKey: '',
  isRegistered: false,
  isExecutable: false,
  capability: 'unknown',
}))
