# CRM frontend module

This module currently provides ten CRM management routes registered by the backend `PageRegistry`:

- Customer: `Crm/Customer/Index` (`/crm/customers`)
- Contact: `Crm/Contact/Index` (`/crm/contacts`)
- Activity: `Crm/Activity/Index` (`/crm/activities`)
- Lead: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity: `Crm/Opportunity/Index` (`/crm/opportunities`)
- Task: `Crm/Task/Index` (`/crm/tasks`)
- Quote: `Crm/Quote/Index` (`/crm/quotes`)
- Sales Order: `Crm/SalesOrder/Index` (`/crm/sales-orders`)
- Shipment: `Crm/Shipment/Index` (`/crm/shipments`)
- Prospecting: `Crm/Prospecting/Index` (`/crm/prospecting`)
- View root: `src/modules/crm/views/crm/<resource>/index.vue`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: resource-scoped `create`, `update`, and `delete`; Leads also use `crm_leads.convert`; Quotes and Shipments expose `create` / `execute`, and Sales Orders expose `execute`.

Customer, Contact, Activity, Lead, Opportunity and Task pages use typed API contracts and the shared API facade at `@/api`. Contacts support customer-scoped filtering, primary-contact designation, email, phone and WhatsApp channels. Activities record calls, emails, meetings, notes, WhatsApp and other interactions against customers, contacts, leads or opportunities. Leads support advisory duplicate detection and conversion to Customer. Opportunities support pipeline stages, amount/currency, close date, probability and loss-reason validation. Tasks support related records, due dates, priorities, assignees and status transitions.

All seven supported locale files include the CRM menu and management strings. Backend page registration is covered by `CrmPageRegistryViewWiringTests`.


Quotation, Sales Order and Shipment backend workflows expose typed frontend API clients and route-contract tests under `src/api/modules/crm`. Their management pages include quotation creation/status handling, quote detail, previous-version navigation and multi-line revision editing, accepted-quote conversion to a sales order, sales-order status and fulfillment-line review, and shipment creation/dispatch/delivery. Shipment creation loads eligible orders, lets users select multiple order lines, displays each line's available-to-ship quantity and validates the requested quantities before the backend's transactional reservation check. Shipment rows also expose a detail view with carrier/tracking metadata and the persisted shipment-line quantities.


Prospecting uses the `CrmProspecting` dynamic API for campaigns, immutable strategy versions, run history, candidate review and explicit conversion of approved candidates to existing CRM Leads. Candidate discovery is separate from Lead creation; duplicate candidates require an explicit review override.
