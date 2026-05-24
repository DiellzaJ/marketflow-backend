# marketflow-backend

## Configuration

Copy `.env.example` to `.env` for local development and keep real credentials in `.env` or your deployment environment variables.

Do not store secrets in tracked files such as `appsettings.json`, `appsettings.Development.json`, or `launchSettings.json`.

## Deployment Notes

Require HTTPS for API traffic in deployed environments. Company onboarding receives the initial CompanyAdmin password in plaintext over the request body before the backend hashes it with BCrypt.

## Users API

Operational users (`MainOperator`, `DepartmentManager`, `InventoryEmployee`, and `Seller`) must be created with a `marketId`. They can also include a `departmentId` for an optional department assignment inside that market. `CompanyAdmin` users should omit both fields.

User responses include the current active assignment when one exists:

```json
{
  "id": 42,
  "fullName": "Store Seller",
  "email": "seller@freshmarket.test",
  "roleName": "Seller",
  "isActive": true,
  "assignment": {
    "marketId": 3,
    "marketName": "Central Market",
    "departmentId": 4,
    "departmentName": "Produce"
  }
}
```

When a user is assigned only to a market, `departmentId` and `departmentName` are `null`. Company users without an active staff assignment return `assignment: null`.

Assignment summaries are read from each company's tenant schema. If a tenant schema is temporarily missing assignment tables or lacks read privileges, user list responses still return the global user records and omit assignment summaries for that affected schema while the backend logs a warning. User creation still requires a writable tenant `staff_assignments` table so assignment insert failures roll back the created user.

For companies with large user counts, monitor the user list assignment lookup query against `staff_assignments`. The backend batches assignment summary lookups per tenant schema in groups of 1,000 users and uses the existing `idx_staff_user` index.

Controller tests cover the user API response shape for assignment summaries. Add database-backed integration or E2E coverage when a test Postgres tenant schema is available in CI.

## Dashboard API

`GET /api/dashboard/sales-summary` returns the current user's scoped sales totals for dashboard surfaces:

- `totalRevenue`
- `totalSales`
- `totalItemsSold`
- `averageSaleAmount`

Optional `from` and `to` query parameters filter by inclusive sale date, for example `/api/dashboard/sales-summary?from=2026-05-01&to=2026-05-31`. The endpoint uses the same `sales:read` authorization policy, tenant schema resolution, and role assignment scoping as the sales endpoints.

## Products API

Product removal uses soft-deactivation. `DELETE /api/products/{id}` remains supported for existing clients, but it marks the product inactive instead of deleting the row. New clients should prefer the explicit state endpoints:

- `POST /api/products/{id}/deactivate`
- `POST /api/products/{id}/reactivate`

Default product list and detail responses only return active products. Product list callers can opt into inactive data with `includeInactive=true`, or request only inactive products with `isActive=false`. Generic product `PUT` and `PATCH` requests do not change `IsActive`; use the explicit deactivate/reactivate endpoints for state transitions.

## Inventory Authorization

The backend inventory matrix is defined in `InventoryPermissionMatrix` and uses these permission keys for API policies, seeded role permissions, and frontend sidebar/route guard parity:

| Role | Scope | View inventory | Create records | Update stock | Adjust stock | Delete records | View movements | Transfer stock |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RootAdmin | No tenant inventory access | No | No | No | No | No | No | No |
| CompanyAdmin | All company inventory | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| MainOperator | Assigned market inventory | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| DepartmentManager | Assigned department inventory | Yes | No | Yes | Yes | No | Yes | Yes |
| InventoryEmployee | Assigned market or department inventory | Yes | No | Yes | Yes | No | Yes | No |
| Seller | Assigned POS market availability only | Yes | No | No | No | No | No | No |

Permission keys:

- `inventory:read` gates inventory visibility and POS availability reads.
- `inventory:create` gates creating inventory records.
- `stock:update` gates full stock updates, including `PUT /api/inventory/{id}`.
- `stock:adjust` gates stock adjustments, including `PATCH /api/inventory/{id}`.
- `inventory:delete` gates deleting inventory records.
- `inventory-movements:read` gates inventory movement history.
- `stock:transfer` gates transfers between allowed inventory scopes.

Scope rules are enforced from the user's tenant assignment: `CompanyAdmin` has company-wide scope, `MainOperator` is limited to the assigned market, `DepartmentManager` is limited to the assigned department, `InventoryEmployee` is limited to the assigned market or department when present, and `Seller` only reads availability for POS flows. `RootAdmin` is a platform role and has no tenant inventory access by default.

## Product Persistence Notes

Tenant product rows use `is_active` as soft-delete state. This preserves existing foreign-key references from inventory, purchase items, and sale items, so historical operational data remains valid after a product is deactivated.

Product active-state updates are conditional (`is_active <> target_state`) so concurrent deactivate/reactivate requests can distinguish an actual state transition from an already-active or already-inactive conflict.

An opt-in live smoke test covers the graceful fallback path for a tenant schema that is present but missing `staff_assignments`. To run it, point `MARKETFLOW_TEST_DB_CONNECTION_STRING` at a disposable Postgres database that has the global MarketFlow migrations applied, then run the normal test command:

```bash
MARKETFLOW_TEST_DB_CONNECTION_STRING="Host=localhost;Port=5432;Database=marketflow_test;Username=postgres;Password=postgres" \
dotnet test tests/MarketFlow.Api.Tests/MarketFlow.Api.Tests.csproj
```

For production deployments with multi-region or cross-database tenancy, verify that the API role has read access to each tenant schema's `markets`, `departments`, and `staff_assignments` tables, write access to `staff_assignments` for user creation, and acceptable latency for per-schema assignment summary lookups.
