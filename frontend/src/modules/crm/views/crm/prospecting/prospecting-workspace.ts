import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * 跨页面联动：规划与调度页创建 Run / 变更计划后，通知获客工作区刷新。
 * 只保留一个刷新令牌，不缓存业务数据——各页面仍自行请求真实数据源。
 */
export const useProspectingWorkspaceStore = defineStore('prospecting-workspace', () => {
  const refreshToken = ref(0)

  function requestRefresh() {
    refreshToken.value += 1
  }

  return { refreshToken, requestRefresh }
})
