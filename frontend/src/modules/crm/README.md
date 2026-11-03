# CRM frontend module

This module provides five CRM management routes registered by the backend `PageRegistry`:

- Customer: `Crm/Customer/Index` (`/crm/customers`)
- Contact: `Crm/Contact/Index` (`/crm/contacts`)
- Lead: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity: `Crm/Opportunity/Index` (`/crm/opportunities`)
- Task: `Crm/Task/Index` (`/crm/tasks`)
- View root: `src/modules/crm/views/crm/<resource>/index.vue`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: resource-scoped `create`, `update`, and `delete`; Leads also use `crm_leads.convert`.

Customer, Contact, Lead, Opportunity and Task pages use typed API contracts and the shared API facade at `@/api`. Contacts support customer-scoped filtering, paging, create/update/delete, primary-contact designation and the full set of contact channels. Leads support advisory duplicate detection and conversion to Customer. Opportunities support pipeline stages, amount/currency, close date, probability and loss-reason validation. Tasks support related records, due dates, priorities, assignees and status transitions.

All seven supported locale files include the CRM menu and contact-management strings. Backend page registration is covered by `CrmPageRegistryViewWiringTests`.
