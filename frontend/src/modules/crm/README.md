# CRM frontend module

This module currently provides six CRM management routes registered by the backend `PageRegistry`:

- Customer: `Crm/Customer/Index` (`/crm/customers`)
- Contact: `Crm/Contact/Index` (`/crm/contacts`)
- Activity: `Crm/Activity/Index` (`/crm/activities`)
- Lead: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity: `Crm/Opportunity/Index` (`/crm/opportunities`)
- Task: `Crm/Task/Index` (`/crm/tasks`)
- View root: `src/modules/crm/views/crm/<resource>/index.vue`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: resource-scoped `create`, `update`, and `delete`; Leads also use `crm_leads.convert`.

Customer, Contact, Activity, Lead, Opportunity and Task pages use typed API contracts and the shared API facade at `@/api`. Contacts support customer-scoped filtering, primary-contact designation, email, phone and WhatsApp channels. Activities record calls, emails, meetings, notes, WhatsApp and other interactions against customers, contacts, leads or opportunities. Leads support advisory duplicate detection and conversion to Customer. Opportunities support pipeline stages, amount/currency, close date, probability and loss-reason validation. Tasks support related records, due dates, priorities, assignees and status transitions.

All seven supported locale files include the CRM menu and management strings. Backend page registration is covered by `CrmPageRegistryViewWiringTests`.


Quotation, Sales Order and Shipment backend workflows now expose typed frontend API clients and route-contract tests under `src/api/modules/crm`. Their management pages are now registered in the CRM page registry and include quotation creation/status handling, sales-order status and fulfillment-line review, and shipment creation/dispatch/delivery. Shipment creation currently accepts one order line per draft; multi-line editing and quote-to-order conversion UX remain follow-up work.
