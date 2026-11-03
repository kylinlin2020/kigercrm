import { describe, expect, it } from 'vitest'
import * as crmBarrel from './index'
import { crmActivityApi } from './activity'
import { crmContactApi } from './contact'
import { crmCustomerApi } from './customer'
import { crmLeadApi } from './lead'
import { crmOpportunityApi } from './opportunity'
import { crmTaskApi } from './task'

describe('CRM API barrel', () => {
  it('exports all CRM resource clients from the shared CRM module entry', () => {
    expect(crmBarrel.crmActivityApi).toBe(crmActivityApi)
    expect(crmBarrel.crmContactApi).toBe(crmContactApi)
    expect(crmBarrel.crmCustomerApi).toBe(crmCustomerApi)
    expect(crmBarrel.crmLeadApi).toBe(crmLeadApi)
    expect(crmBarrel.crmOpportunityApi).toBe(crmOpportunityApi)
    expect(crmBarrel.crmTaskApi).toBe(crmTaskApi)
  })

  it('exports the CRM workflow enums from the same entry', () => {
    expect(crmBarrel.CrmActivityType).toBeDefined()
    expect(crmBarrel.CrmLeadStatus).toBeDefined()
    expect(crmBarrel.CrmOpportunityStage).toBeDefined()
    expect(crmBarrel.CrmTaskStatus).toBeDefined()
  })
})
