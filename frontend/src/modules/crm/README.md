# CRM frontend module

This module currently provides the CRM Lead management route registered by the backend `PageRegistry`:

- Component: `Crm/Lead/Index`
- Resolved view: `src/modules/crm/views/crm/lead/index.vue`
- Menu route: `/crm/leads`
- Page permission: `crm.leads.read` (backend menu permission)
- Button permissions: `crm_leads.create`, `crm_leads.update`, `crm_leads.delete`, `crm_leads.convert`

The Lead page supports paged search, create/update, advisory duplicate detection with explicit confirmation, conversion to Customer, and deletion. It uses the shared API facade at `@/api`.

All seven supported locale files are required by the frontend module validation gate. The backend PageRegistry also declares Customers, Opportunities and Tasks; their screens are not yet implemented, so CRM menu seeding must remain disabled until those routes have real views.
