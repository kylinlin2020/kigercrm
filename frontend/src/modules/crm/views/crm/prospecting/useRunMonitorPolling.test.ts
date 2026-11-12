import type { Ref } from 'vue'
import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'

import { RUN_MONITOR_POLLING_INTERVAL_MS, useRunMonitorPolling } from './useRunMonitorPolling'

function mountPolling(shouldPoll: Ref<boolean>, onTick: () => Promise<void>) {
  return mount(defineComponent({
    setup() {
      useRunMonitorPolling({ shouldPoll, onTick })
      return () => h('div')
    },
  }))
}

function setVisibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
}

describe('useRunMonitorPolling', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    Reflect.deleteProperty(document, 'visibilityState')
  })

  it('ticks at the fixed interval only while shouldPoll is true', async () => {
    const shouldPoll = ref(true)
    const onTick = vi.fn().mockResolvedValue(undefined)
    const wrapper = mountPolling(shouldPoll, onTick)

    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    expect(onTick).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS * 2)
    expect(onTick).toHaveBeenCalledTimes(3)

    shouldPoll.value = false
    await nextTick()
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS * 3)
    expect(onTick).toHaveBeenCalledTimes(3)

    wrapper.unmount()
  })

  it('starts immediately when shouldPoll is already true at mount', async () => {
    const shouldPoll = ref(true)
    const onTick = vi.fn().mockResolvedValue(undefined)
    const wrapper = mountPolling(shouldPoll, onTick)

    // No tick before the first interval elapses — polling must not fire on setup.
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS - 1)
    expect(onTick).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)
    expect(onTick).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  it('skips ticks while the page is hidden and resumes when visible again', async () => {
    const shouldPoll = ref(true)
    const onTick = vi.fn().mockResolvedValue(undefined)
    const wrapper = mountPolling(shouldPoll, onTick)

    setVisibility('hidden')
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS * 2)
    expect(onTick).not.toHaveBeenCalled()

    setVisibility('visible')
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    expect(onTick).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  it('does not re-enter while the previous tick is still in flight', async () => {
    const shouldPoll = ref(true)
    let release!: () => void
    const onTick = vi.fn().mockImplementation(() => new Promise<void>((resolve) => {
      release = resolve
    }))
    const wrapper = mountPolling(shouldPoll, onTick)

    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    expect(onTick).toHaveBeenCalledTimes(1)

    release()
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    expect(onTick).toHaveBeenCalledTimes(2)

    wrapper.unmount()
  })

  it('clears the interval when the component unmounts', async () => {
    const shouldPoll = ref(true)
    const onTick = vi.fn().mockResolvedValue(undefined)
    const wrapper = mountPolling(shouldPoll, onTick)

    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS * 3)
    expect(onTick).not.toHaveBeenCalled()
  })

  it('does not start again after dispose even if shouldPoll flips back', async () => {
    const shouldPoll = ref(true)
    const onTick = vi.fn().mockResolvedValue(undefined)
    const wrapper = mountPolling(shouldPoll, onTick)

    shouldPoll.value = false
    await nextTick()
    shouldPoll.value = true
    await nextTick()
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS)
    expect(onTick).toHaveBeenCalledTimes(1)

    wrapper.unmount()
    shouldPoll.value = false
    await nextTick()
    shouldPoll.value = true
    await nextTick()
    await vi.advanceTimersByTimeAsync(RUN_MONITOR_POLLING_INTERVAL_MS * 2)
    expect(onTick).toHaveBeenCalledTimes(1)
  })
})
