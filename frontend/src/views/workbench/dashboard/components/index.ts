import type { Component } from 'vue'
import { markRaw } from 'vue'
import AnnouncementWidget from './AnnouncementWidget.vue'
import CrmFollowUpWidget from './charts/CrmFollowUpWidget.vue'
import CrmFunnelWidget from './charts/CrmFunnelWidget.vue'
import CrmOverviewWidget from './charts/CrmOverviewWidget.vue'
import CrmSourcesWidget from './charts/CrmSourcesWidget.vue'
import CrmStagesWidget from './charts/CrmStagesWidget.vue'
import CrmTeamWidget from './charts/CrmTeamWidget.vue'
import ClockWidget from './ClockWidget.vue'
import FavoritesWidget from './FavoritesWidget.vue'
import TodoWidget from './TodoWidget.vue'
import WelcomeWidget from './WelcomeWidget.vue'

/** 小组件定义：键、i18n 标题/描述键、图标、默认宽度（12 栅格）、组件、可选权限码 */
export interface WidgetDef {
  key: string
  titleKey: string
  descKey: string
  icon: string
  defaultSpan: number
  component: Component
  /** 可选权限码：声明后仅拥有该权限的用户可见/可添加此小组件（缺省人人可见） */
  permission?: string
}

function widget(
  key: string,
  i18nKey: string,
  icon: string,
  defaultSpan: number,
  component: Component,
  permission?: string,
): WidgetDef {
  return {
    key,
    titleKey: `workbench.widgets.${i18nKey}.title`,
    descKey: `workbench.widgets.${i18nKey}.desc`,
    icon,
    defaultSpan,
    component: markRaw(component),
    permission,
  }
}

/**
 * 小组件登记表。
 * 仪表盘上的业务指标小组件（crm-*）走 CrmDashboardQueryService 的真实数据，
 * 由页面按钮码 crm_prospecting.dashboard_widgets 门控——无 CRM 权限的用户看不到也不会请求这些数据。
 * 欢迎/时钟/收藏/待办/公告是通用能力，不依赖 CRM，所有用户可见。
 */
export const WIDGETS: WidgetDef[] = [
  widget('welcome', 'welcome', 'lucide:sparkles', 4, WelcomeWidget),
  widget('clock', 'clock', 'lucide:clock', 4, ClockWidget),
  widget('favorites', 'favorites', 'lucide:star', 4, FavoritesWidget),
  widget('todo', 'todo', 'lucide:check-square', 3, TodoWidget),
  widget('announcement', 'announcement', 'lucide:megaphone', 9, AnnouncementWidget),
  widget('crm-overview', 'crm_overview', 'lucide:gauge', 4, CrmOverviewWidget, 'crm_prospecting.dashboard_widgets'),
  widget('crm-funnel', 'crm_funnel', 'lucide:filter', 4, CrmFunnelWidget, 'crm_prospecting.dashboard_widgets'),
  widget('crm-sources', 'crm_sources', 'lucide:pie-chart', 4, CrmSourcesWidget, 'crm_prospecting.dashboard_widgets'),
  widget('crm-stages', 'crm_stages', 'lucide:chart-pie', 4, CrmStagesWidget, 'crm_prospecting.dashboard_widgets'),
  widget('crm-team', 'crm_team', 'lucide:users', 6, CrmTeamWidget, 'crm_prospecting.dashboard_widgets'),
  widget('crm-follow-up', 'crm_follow_up', 'lucide:heart-pulse', 6, CrmFollowUpWidget, 'crm_prospecting.dashboard_widgets'),
]

export const WIDGET_MAP: Record<string, WidgetDef> = Object.fromEntries(WIDGETS.map(widget => [widget.key, widget]))

/** 看板项：小组件键 + 宽度 */
export interface BoardItem {
  key: string
  span: number
}

/**
 * 默认看板，每行凑满 12 栅格：
 * 欢迎(4) 时间(4) 收藏入口(4) / 便签待办(3) 公告轮播(9) /
 * CRM 概览(4) 线索漏斗(4) 候选来源(4) / 商机阶段(4) 团队排行(6) 跟进健康度(6)
 */
export const DEFAULT_BOARD: BoardItem[] = WIDGETS.map(widget => ({ key: widget.key, span: widget.defaultSpan }))
