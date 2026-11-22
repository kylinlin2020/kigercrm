import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useProspectingWorkspaceStore } from './prospecting-workspace'

describe('prospectingWorkspaceStore', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('increments a refresh token so the workspace page can react to cross-page changes', () => {
    const store = useProspectingWorkspaceStore()
    expect(store.refreshToken).toBe(0)
    store.requestRefresh()
    expect(store.refreshToken).toBe(1)
    store.requestRefresh()
    expect(store.refreshToken).toBe(2)
  })
})
