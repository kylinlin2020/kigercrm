import type { Ref } from 'vue'
import { getCurrentInstance, onBeforeUnmount, watch } from 'vue'

export const RUN_MONITOR_POLLING_INTERVAL_MS = 5_000

export interface RunMonitorPollingOptions {
  /** 轮询开关：选中的 Run 处于 Queued / Running / WaitingForExtractions 时为 true。 */
  shouldPoll: Ref<boolean>
  /** 每轮刷新动作（Run 状态 + 监控台账双取）；返回 Promise 以便测试与顺序保证。 */
  onTick: () => Promise<void>
  intervalMs?: number
}

/**
 * Run 监控轮询：shouldPoll 为真时按固定间隔触发 onTick。
 * - 页面隐藏（visibilityState = hidden）时跳过本轮，不制造后台请求；
 * - 上一轮 onTick 未完成时不重入，避免慢请求堆叠；
 * - shouldPoll 转假或组件卸载时清理定时器，不留后台 interval。
 */
export function useRunMonitorPolling({ shouldPoll, onTick, intervalMs = RUN_MONITOR_POLLING_INTERVAL_MS }: RunMonitorPollingOptions) {
  let timer: ReturnType<typeof setInterval> | null = null
  let ticking = false
  let disposed = false

  async function tick() {
    if (disposed || ticking || document.visibilityState === 'hidden')
      return
    ticking = true
    try {
      await onTick()
    }
    finally {
      ticking = false
    }
  }

  function stop() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  function start() {
    if (disposed || timer !== null)
      return
    timer = setInterval(() => {
      void tick()
    }, intervalMs)
  }

  const unwatch = watch(shouldPoll, (active) => {
    if (active)
      start()
    else
      stop()
  }, { immediate: true })

  function dispose() {
    disposed = true
    stop()
    unwatch()
  }

  if (getCurrentInstance())
    onBeforeUnmount(dispose)

  return { dispose, tick }
}
