# Haituo → KIGER CRM target-side second-pass audit

> Audit branch: `work/d-migration-audit`
> Target tree inspected: `work/d-migration-audit`
> Method: recursive target-tree inventory and inspection of concrete module entry points. This is a structural audit, not runtime or UI acceptance.

## Findings

### Confirmed target implementation candidates

| Capability | Concrete target evidence | Audit status |
|---|---|---|
| CRM customer/contact/lead/opportunity/activity/task | `XiHan.BasicApp.Crm/Application/AppServices/Crm*AppService.cs`, corresponding contracts, DTOs, query services and domain services | Core CRM application surface exists; business equivalence still requires source-vs-target behavior tests |
| Quote/order/shipment | `CrmQuoteAppService.cs`, `CrmSalesOrderAppService.cs`, `CrmShipmentAppService.cs` and query/DTO counterparts | Core trade document workflow exists; source-specific OCR/import path not established |
| Prospecting campaigns/strategies/runs/candidates | `CrmProspectingAppService.cs`, Prospecting domain/repository/infrastructure families | Main prospecting surface exists; async provider execution remains a separate runtime gate |
| Knowledge documents | AI module `KnowledgeDocumentAppService`, `KnowledgeDocumentQueryService`, `SysKnowledgeDocument`, repository and domain service | Document CRUD/workflow candidate exists; source knowledge asset review/version/attachments/market tags and retraining triggers are not proven equivalent |
| Chat internal conversations | Chat module Conversation/Message/Member/Reaction entities, repositories, AppService and SignalR Hub | Internal chat exists; WhatsApp/WeCom channels are not implied |

## Not found as dedicated target module/file families in this tree scan

The following source capabilities did not surface as dedicated target implementation families under the inspected module paths. This is a negative structural finding only; generic platform services or external integration may still cover parts of them.

| Source capability | Source evidence from inventory | Target-side finding | Priority |
|---|---|---|---|
| Online exam / question bank / score / retake | `exam-system-test.ts` | No dedicated Exam/QuestionBank/ExamAttempt module family found | High |
| Business-card OCR → reviewed Lead | `business-card-ocr.ts`, `business-card-ocr-test.ts` | No dedicated BusinessCard/OCR feature family found | High |
| Shipment/trade-document OCR and import | `shipment-ocr.ts`, `trade-document-import.ts` | Shipment CRUD exists, but OCR/import workflow family not found | High |
| CRM-specific import/export | `mysql-data-import.ts`, frontend dump import | CRM CRUD exists, but no dedicated CRM import/export preview, mapping, error-row retry or export job family found in CRM module | High |
| Market opportunity/background research reports | `market-opportunity-*.ts`, `customer-intelligence.ts` | No dedicated MarketOpportunity/ResearchReport family found | Medium |
| Outreach sequences / development-email automation | `outreach-sequences.ts`, `prospect-outreach.ts` | No dedicated OutreachSequence/Suppression/Approval workflow family found in CRM module | High |
| WeCom command gateway / connector | `wecom-command-gateway.ts`, `wecom-connector-driver.ts` | No dedicated WeCom connector/command gateway family found | Medium |
| WhatsApp provider/account/inbox/outbox | `whatsapp-plugin/` | Chat internal messaging exists, but no WhatsApp provider/account/inbox/outbox family found | High |

## Important interpretation

- A target file-name scan cannot prove a feature is absent from all generic modules; before migration, inspect dynamic APIs, workflow, integration, file storage, and UI routes for reusable capability.
- A candidate implementation is not equivalent migration. Completion still requires matching API, UI, permissions, tenant boundaries, persistence, data migration, tests and operational evidence.
- The user's explicit scope correction remains in force: do not create a second CRM integration workstream (the earlier matrix's item 6); CRM is already integrated in the target platform.
- External email, WhatsApp messages, and website publication must remain behind explicit human approval. No source-side automation should bypass this approval boundary.

## Next implementation order

1. Finish A's durable async extraction consumer and parent Run continuation before enabling async providers.
2. Implement C's Meta webhook receiver and persistent idempotent Inbox against the target's existing host conventions; keep outbound approval separate.
3. For D, inspect dynamic API, Workflow, file storage and frontend routes before building a dedicated exam/OCR/import module. Prefer extending existing CRM/AI workflows over duplicating generic platform services.
4. Prioritize CRM import/export preview + field mapping and business-card OCR-to-Lead as practical migration slices; keep exam/question-bank as a separate bounded module.
5. Add tests and migration scripts for each slice before marking any row complete.
