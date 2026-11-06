# Branch divergence and integration audit — 2026-11-06

## Scope

This audit compares `main` and `kiger-dev` without modifying either ref. It is intended to prevent accidental loss of the CRM migration work while the pull request is being prepared.

## Observed refs

At the time of inspection:

- `main`: `74cf1f2c0b8f02fe0f401452dd25b18917ff037a`
- `kiger-dev`: `9546162670bf4f6822bf794e5212dff1aaa35f06`
- Common merge base: `d8ce32d2e2d0b7337581f57603fc0a3716c1e102`
- Compare result: diverged; `kiger-dev` is 2,457 commits ahead and 6 commits behind `main`.
- PR #1 remains open and Draft; it is not merged.
- The compare API reports 479 changed files in the PR.

The six commits on `main` after the merge base are:

1. `1363404` — add fenced candidate/evidence/score persistence service.
2. `32f620d` — revert an unapproved candidate-persistence file from `main`.
3. `5adb20f` — add regression guards for atomic async result-page persistence.
4. `8c50685` — document atomic async result-page persistence.
5. `7243748` — atomically fail incomplete extraction after the final page.
6. `74cf1f2` — validate async page candidate identity mapping.

## Overlapping files requiring reconciliation

The two refs both contain different blob versions for these files:

- `backend/src/modules/XiHan.BasicApp.Crm/Infrastructure/Prospecting/CrmProspectingCandidateFactory.cs`
- `backend/src/modules/XiHan.BasicApp.Crm/Infrastructure/Repositories/CrmProspectingExtractionJobRepository.cs`
- `backend/test/XiHan.BasicApp.Api.Tests/CrmProspectingApplicationWiringTests.cs`
- `docs/kiger/PROVIDER-ASYNC-EXTRACTION-DESIGN.md`

The branch versions are not byte-identical to the current `main` versions. This is a concrete overlap, but it is not by itself proof of a textual merge conflict. A normal merge/rebase must not be attempted blindly.

## Safe integration decision

1. Keep `main` untouched.
2. Keep `kiger-dev` as the active development head; do not force-push or rewrite its 2,457-commit history.
3. Reconcile the four overlapping files by semantic diff, preserving the more complete fenced persistence implementation and all regression assertions.
4. Verify the final-page transaction invariant: candidate/evidence writes and cursor/final-state CAS must commit or roll back together; any accumulated record failure must prevent a successful Completed state.
5. Verify candidate identity is one-to-one with normalized records in the fetched page and matches Run/provider identity.
6. Only after source reconciliation, update the PR branch from the current base using a non-force-push merge commit, then run CI. If GitHub reports conflicts, resolve only the four reviewed paths and inspect the resulting diff before pushing.

## CI status and validation boundary

- The workflow file now includes frontend unit tests and a production build in addition to type-check, lint, px gate and module/locale validation.
- No GitHub Actions run was returned for the latest branch commit during this audit. Therefore no CI pass is claimed.
- No local .NET restore/build/test or frontend install/build/test was executed in this GitHub-only environment.
- Windows runtime and PostgreSQL acceptance remain a later gate.

## Current implementation boundary

The async extraction foundations include provider contracts, durable intent, PostgreSQL claims/fencing, state/poll policies, page validators, and atomic page persistence services. The production execution gap remains: there is no registered async provider adapter and no dedicated worker that claims jobs, submits/polls/fetches pages, persists them, and resumes/completes the parent Run. The async path must remain unavailable to production Runs until that orchestration is implemented and tested end-to-end.
