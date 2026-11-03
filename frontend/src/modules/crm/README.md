# CRM frontend module

This module provides all four CRM management routes registered by the backend `PageRegistry`:

- Customer component: `Crm/Customer/Index` (`/crm/customers`)
- Lead component: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity component: `Crm/Opportunity/Index` (`/crm/opportunities`)
- Task component: `Crm/Task/Index` (`/crm/tasks`)
- View root: `src/modules/crm/views/crm/<resource>/index.vue`
- Page routes: `/crm/customers`, `/crm/leads`, `/crm/opportunities`, `/crm/tasks`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: resource-scoped `create`, `update`, and `delete`; Leads also use `crm_leads.convert`

The Customer page supports paged search, filtering by region and industry, create/update, and deletion. The Lead page supports paged search, create/update, advisory duplicate detection with explicit confirmation, conversion to Customer, and deletion. The Opportunity page supports pipeline stage filtering, create/update, amount/currency, close date, probability, loss reason validation, and deletion. The Task page supports status and overdue filtering, related-record references, due dates, priority, assignee, create/update/delete, and status transitions. All four use typed API contracts and the shared API facade at `@/api`.

All seven supported locale files are required by the frontend module validation gate. All four backend PageRegistry routes now have corresponding frontend views. CRM menu seeding can proceed to integration testing, subject to the existing permission and route-resolution checks.
