import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const VIEW_DIR = join(process.cwd(), 'src', 'modules', 'crm', 'views', 'crm', 'prospecting')

function source(name: string) {
  return readFileSync(join(VIEW_DIR, name), 'utf8')
}

const index = source('index.vue')
const bulkActionBar = source('BulkActionBar.vue')
const candidateTable = source('CandidateTable.vue')
const reviewDetailPanel = source('ReviewDetailPanel.vue')
const superSearchPanel = source('SuperSearchPreviewPanel.vue')

describe('prospecting page component split', () => {
  it('keeps shared state in the page and delegates the three extracted views', () => {
    expect(index).toContain('import BulkActionBar from \'./BulkActionBar.vue\'')
    expect(index).toContain('import CandidateTable from \'./CandidateTable.vue\'')
    expect(index).toContain('import ReviewDetailPanel from \'./ReviewDetailPanel.vue\'')
    expect(index).toContain('<BulkActionBar')
    expect(index).toContain('<CandidateTable')
    expect(index).toContain('<ReviewDetailPanel')
    expect(index).toContain('import { useProspectingLabels } from \'./prospecting-labels\'')

    // The extracted markup must not drift back into the page.
    expect(index).not.toContain('class="bulk-action-bar"')
    expect(index).not.toContain('class="pagination-bar"')
    expect(index).not.toContain('class="panel review-detail"')
  })

  it('keeps bulk commands permission-gated and free of direct API calls', () => {
    expect(bulkActionBar).toContain('canExecute')
    expect(bulkActionBar).toContain('canCreateLead')
    for (const action of ['approve', 'reject', 'convert', 'shortlist', 'unshortlist'])
      expect(bulkActionBar).toContain(`emit('action', '${action}')`)
    expect(bulkActionBar).toContain('CrmProspectingBulkOutcome')
    expect(bulkActionBar).not.toContain('crmProspectingApi')
    expect(bulkActionBar).toContain('useI18n')
  })

  it('keeps candidate selection, review and pagination in the table component', () => {
    expect(candidateTable).toContain('import { useProspectingLabels } from \'./prospecting-labels\'')
    expect(candidateTable).toContain('emit(\'toggleAll\'')
    expect(candidateTable).toContain('emit(\'toggle\'')
    expect(candidateTable).toContain('emit(\'page\'')
    expect(candidateTable).toContain('CrmProspectingCandidateStatus')
    expect(candidateTable).toContain('canExecute')
    expect(candidateTable).toContain('canCreateLead')
    expect(candidateTable).toContain('shortlistedAt')
    expect(candidateTable).not.toContain('crmProspectingApi')
  })

  it('keeps evidence rendering behind the HTTPS-only URL guard', () => {
    expect(reviewDetailPanel).toContain('safeEvidenceUrl')
    expect(reviewDetailPanel).toContain('url.protocol !== \'https:\'')
    expect(reviewDetailPanel).toContain('scoreGates')
    expect(reviewDetailPanel).toContain('useI18n')
    expect(reviewDetailPanel).not.toContain('crmProspectingApi')
  })

  it('keeps the Super Search preview read-only, permission-gated and keyboard reachable', () => {
    expect(index).toContain('import SuperSearchPreviewPanel from \'./SuperSearchPreviewPanel.vue\'')
    expect(index).toContain('<SuperSearchPreviewPanel')
    expect(superSearchPanel).toContain('usePermission')
    expect(superSearchPanel).toContain('hasPermission(\'crm_prospecting.execute\')')
    expect(superSearchPanel).toContain('previewSuperSearch')
    expect(superSearchPanel).not.toContain('startRun')
    expect(superSearchPanel).not.toContain('bulkReviewCandidates')
    expect(superSearchPanel).toContain('role="status"')
    expect(superSearchPanel).toContain('aria-live="polite"')
    expect(superSearchPanel).toContain('<details')
    expect(superSearchPanel).toContain('preview.value = null')
    expect(superSearchPanel).toContain('useI18n')
  })

  it('keeps Super Search missions durable and reuses the page run loader after execution', () => {
    expect(superSearchPanel).toContain('saveSuperSearchMission')
    expect(superSearchPanel).toContain('superSearchMissions')
    expect(superSearchPanel).toContain('createRunFromSuperSearchMission')
    expect(superSearchPanel).toContain('defineEmits')
    expect(superSearchPanel).toContain('runCreated: [runId: ApiId]')
    expect(superSearchPanel).toContain('mission.runId')
    expect(index).toContain('@run-created="loadWorkspace"')
  })
})
