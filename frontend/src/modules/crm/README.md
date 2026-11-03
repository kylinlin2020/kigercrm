# CRM frontend module

This module currently provides the CRM Customer, Lead, and Opportunity management routes registered by the backend `PageRegistry`:

- Customer component: `Crm/Customer/Index` (`/crm/customers`)
- Lead component: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity component: `Crm/Opportunity/Index` (`/crm/opportunities`)
- View root: `src/modules/crm/views/crm/<resource>/index.vue`
- Page routes: `/crm/customers`, `/crm/leads`, `/crm/opportunities`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: resource-scoped `create`, `update`, and `delete`; Leads also use `crm_leads.convert`

The Customer page supports paged search, filtering by region and industry, create/update, and deletion. The Lead page supports paged search, create/update, advisory duplicate detection with explicit confirmation, conversion to Customer, and deletion. The Opportunity page supports pipeline stage filtering, create/update, amount/currency, close date, probability, loss reason validation, and deletion. All three use typed API contracts and the shared API facade at `@/api`.

All seven supported locale files are required by the frontend module validation gate. The backend PageRegistry also declares Tasks; its screen is not yet implemented, so CRM menu seeding must remain disabled until the Task route has a real view.
