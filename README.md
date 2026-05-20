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

An opt-in live smoke test covers the graceful fallback path for a tenant schema that is present but missing `staff_assignments`. To run it, point `MARKETFLOW_TEST_DB_CONNECTION_STRING` at a disposable Postgres database that has the global MarketFlow migrations applied, then run the normal test command:

```bash
MARKETFLOW_TEST_DB_CONNECTION_STRING="Host=localhost;Port=5432;Database=marketflow_test;Username=postgres;Password=postgres" \
dotnet test tests/MarketFlow.Api.Tests/MarketFlow.Api.Tests.csproj
```

For production deployments with multi-region or cross-database tenancy, verify that the API role has read access to each tenant schema's `markets`, `departments`, and `staff_assignments` tables, write access to `staff_assignments` for user creation, and acceptable latency for per-schema assignment summary lookups.
