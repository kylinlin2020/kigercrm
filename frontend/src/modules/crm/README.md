# CRM frontend module

This module currently provides the CRM Customer, Lead, and Opportunity management routes registered by the backend `PageRegistry`:

- Customer component: `Crm/Customer/Index` (`/crm/customers`)
- Lead component: `Crm/Lead/Index` (`/crm/leads`)
- Opportunity component: `Crm/Opportunity/Index` (`/crm/opportunities`)
- Resolved view: `src/modules/crm/views/crm/lead/index.vue`
- Menu route: `/crm/leads`
- Page permission: `crm:read` (backend menu permission)
- Button permissions: `crm_leads.create`, `crm_leads.update`, `crm_leads.delete`, `crm_leads.convert`

The Customer page supports paged search, filtering by region and industry, create/update, and deletion. The Lead page supports paged search, create/update, advisory duplicate detection with explicit confirmation, conversion to Customer, and deletion. The Opportunity page supports pipeline stage filtering, create/update, amount/currency, close date, probability, loss reason validation, and deletion. All three use typed API contracts and the shared API facade at `@/api`.

All seven supported locale files are required by the frontend module validation gate. The backend PageRegistry also declares Customers, Opportunities and Tasks; their screens are not yet implemented, so CRM menu seeding must remain disabled until those routes have real views.
