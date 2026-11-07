# Haituo migration — reusable target capability audit

> Branch: `work/d-migration-audit`
> Scope: inspect concrete SaaS platform entry points after the second-pass tree scan.
> This is a source inspection only; it does not claim runtime or UI parity.

## Confirmed reusable platform capabilities

| Platform capability | Concrete target implementation | Reuse boundary |
|---|---|---|
| File upload/download/storage | `Saas/Application/AppServices/Files/FileAppService.cs`, `IFileAppService`, file domain/query/transfer services | Reuse for business-card images, trade documents and OCR artifacts. It does not itself perform OCR, extraction, field confidence scoring, or human correction. |
| Asynchronous export jobs | `Saas/Application/AppServices/Exporting/ExportTaskAppService.cs`, `SysExportTask`, `IExportProvider`, `ExportTaskHostedService` and export writers | Reuse provider/worker infrastructure for CRM exports by registering CRM-specific providers and query projections. Existing generic export plumbing is not proof that CRM-specific columns, permission rules, filters, or parity are complete. |
| Import history | `Saas/Application/AppServices/Profile/ImportHistoryAppService.cs`, `SysImportHistory`, `IImportHistoryAppService` | This is append-only post-import audit reporting. The service comment explicitly says actual import is performed by resource endpoints row-by-row. It is not a file parser, preview, field-mapping engine, validation pipeline, retry queue, or CRM import job. |
| Generic workflow/task infrastructure | `Saas/Application/AppServices/Workflow/TaskAppService.cs`, `TaskDomainService`, workflow business handler contracts | Reuse for approval/task assignment and human review tasks where the business handler semantics fit. It is not automatically an outreach sequence engine, email scheduler, suppression-list manager, or external-send approval ledger. |
| Knowledge document management | AI module KnowledgeDocument application/query/domain/repository families | Reuse for document metadata and knowledge ingestion integration. Verify parser/OCR, versioning, access filtering, source citation and indexing lifecycle before claiming equivalence. |

## Corrections to the migration gap register

1. **CRM export:** do not describe the platform as having no export task family. Generic asynchronous export exists. The remaining gap is CRM-specific provider registration, field/query mapping, authorization, and parity tests.
2. **CRM import:** do not confuse `ImportHistoryAppService` with an import engine. It records results after the frontend/resource endpoints have already performed the import. A CRM import-preview/mapping/validation/retry pipeline remains a distinct gap unless another dedicated implementation is located.
3. **OCR:** generic file transfer is reusable for source documents, but it does not satisfy OCR, confidence, correction, duplicate matching, or reviewed Lead/Shipment creation.
4. **Outreach automation:** generic workflow/task services may support human review and assignment, but do not establish sequence scheduling, contact suppression, opt-out handling, delivery tracking, or approval-before-send.
5. **CRM integration:** no second CRM integration workstream is required; the target already has integrated CRM capabilities.

## Recommended implementation slices

### Slice D1 — CRM export providers
Register CRM-specific `IExportProvider` implementations for supported resource types (initially Leads, Contacts, Customers, Opportunities, Quotes, Orders and Shipments only after checking the source fields). Reuse the existing export worker and writers. Tests must cover permission enforcement, tenant isolation, selected columns, filter snapshot, and CSV/XLSX output.

### Slice D2 — CRM import pipeline
Build a CRM-owned import job and preview contract rather than extending import-history as if it were an engine:
- upload through the existing file service;
- parse supported CSV/XLSX formats with file-size/row/column limits;
- preview and map source headers to allowlisted CRM fields;
- validate rows and show deterministic row-level errors before writes;
- require explicit user confirmation before commit;
- persist per-row outcomes and support idempotent retry of failed rows;
- write append-only import history only after execution;
- test tenant isolation, duplicate detection, rollback/partial-failure policy, and permission boundaries.

### Slice D3 — Business-card OCR to reviewed Lead
Use file storage as the artifact boundary, add an OCR provider abstraction with bounded payload/timeouts, store extracted field candidates with confidence and source coordinates, and require human review before creating or merging CRM contacts/leads. Do not auto-send emails or publish website content from OCR output.

## Status

- Generic file storage: **reusable platform capability found**.
- Generic asynchronous export engine: **reusable platform capability found**.
- Import history: **audit-only capability found; import engine not established**.
- Generic workflow/task: **reusable for human tasks; outreach sequence equivalence not established**.
- CRM-specific import/export, OCR, and outreach sequence behavior: **still requires implementation and tests**.
