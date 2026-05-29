# marketflow-backend

## Configuration

Copy `.env.example` to `.env` for local development and keep real credentials in `.env` or your deployment environment variables.

Do not store secrets in tracked files such as `appsettings.json`, `appsettings.Development.json`, or `launchSettings.json`.

## AI Module

MarketFlow AI features are backend-only. The frontend calls MarketFlow `/api/ai/*` endpoints and never calls OpenAI or Ollama directly. The backend resolves the current tenant, loads tenant-safe business data, performs deterministic calculations in application services, and sends only scoped summary data to the configured `IAiClient` provider for wording, explanations, or summaries.

### Architecture

- Controllers in `MarketFlow.Api.Controllers.AiController` expose the AI API and enforce endpoint policies.
- `AiTenantScopeAuthorizationFilter` applies tenant and assignment scope before the service runs.
- Application services calculate forecasts, recommendations, supplier reliability, and anomalies from backend data first.
- `IAiClient` implementations provide model text only:
  - `FakeAiClient` returns deterministic local JSON/text for development and tests.
  - `OpenAiClient` calls OpenAI chat completions.
  - `OllamaAiClient` calls a local Ollama `/api/chat` server.
- AI responses are treated as explanatory text. The backend does not let the model change calculated quantities, stock levels, supplier metrics, anomaly thresholds, or tenant scope.

### Endpoints

| Endpoint | Purpose | Roles |
| --- | --- | --- |
| `POST /api/ai/chat` | Tenant-safe business assistant response. | `CompanyAdmin`, `MainOperator`, `DepartmentManager` |
| `POST /api/ai/reports/query` | Converts a natural-language question into a scoped report. | `CompanyAdmin`, `MainOperator`, `DepartmentManager` |
| `POST /api/ai/dashboard-summary` | Summarizes dashboard KPIs and recommends actions. | `CompanyAdmin`, `MainOperator`, `DepartmentManager` |
| `POST /api/ai/inventory-forecast` | Calculates inventory demand forecast by product. | `CompanyAdmin`, `MainOperator`, `DepartmentManager`, `InventoryEmployee` |
| `POST /api/ai/inventory/recommendations` | Detects low stock, critical stock, and overstock recommendations. | `CompanyAdmin`, `MainOperator`, `DepartmentManager`, `InventoryEmployee` |
| `POST /api/ai/purchases/recommendations` | Calculates purchase quantities and supplier suggestions. | `CompanyAdmin`, `MainOperator` with purchase and supplier read permissions |
| `POST /api/ai/suppliers/performance` | Calculates supplier reliability and performance insights. | `CompanyAdmin`, `MainOperator` with purchase and supplier read permissions |
| `POST /api/ai/anomalies/detect` | Detects discount, below-cost sale, stock movement, and sales spike anomalies. | `CompanyAdmin`, `MainOperator`, `DepartmentManager` |

`Seller` and `RootAdmin` do not have tenant AI endpoint access by default.

### Request And Response Examples

Inventory forecast request:

```http
POST /api/ai/inventory-forecast
Authorization: Bearer <access-token>
Content-Type: application/json
```

```json
{
  "salesHistoryDays": 30,
  "forecastDays": 14,
  "marketId": 1,
  "departmentId": 2
}
```

Example response:

```json
{
  "succeeded": true,
  "message": "",
  "data": {
    "salesHistoryDays": 30,
    "forecastDays": 14,
    "marketId": 1,
    "departmentId": 2,
    "products": [
      {
        "productId": 10,
        "productName": "Coffee Beans",
        "currentStock": 60,
        "totalQuantitySold": 90,
        "averageDailySales": 3,
        "forecastDemand": 42,
        "daysOfStockRemaining": 20
      }
    ]
  }
}
```

Chat request:

```json
{
  "sessionId": 12,
  "message": "Which products sold best this week?",
  "from": "2026-05-01",
  "to": "2026-05-31",
  "marketId": 1
}
```

Example response:

```json
{
  "succeeded": true,
  "message": "",
  "data": {
    "sessionId": 12,
    "answer": "Coffee Beans led sales for the selected period.",
    "reportType": "TopSellingProducts"
  }
}
```

### Provider Configuration

Provider values are configured with `Ai:Provider` in `appsettings*.json` or `AI_PROVIDER` in the environment. Supported values are `Fake`, `OpenAI`, and `Ollama`. The exact .NET configuration keys are `Ai`, `OpenAi`, and `Ollama`.

```json
{
  "Ai": {
    "Provider": "Fake"
  },
  "OpenAi": {
    "ApiKey": "",
    "Model": "gpt-4.1-mini",
    "BaseUrl": "https://api.openai.com",
    "UseFakeClient": false
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.1"
  }
}
```

Friendly environment variables:

```bash
AI_PROVIDER=Fake
OPENAI_API_KEY=
OPENAI_MODEL=gpt-4.1-mini
OPENAI_BASE_URL=https://api.openai.com
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=llama3.1
```

`MaxOutputTokens` is not currently wired into the backend provider options. Add it to `OpenAiOptions` and the request payload before relying on that setting.

### Fake Provider

Use `Fake` for local development, automated tests, CI, and demos where deterministic responses are preferred. It does not require network access, OpenAI billing, an API key, or a running Ollama instance. Development defaults to `Fake` in `appsettings.Development.json`, and integration tests pin `Ai:Provider` to `Fake`.

```bash
AI_PROVIDER=Fake
dotnet run --project src/MarketFlow.Api/MarketFlow.Api.csproj
```

### OpenAI Provider

Use `OpenAI` when real hosted model responses are needed. Configure an API key in `.env`, a secret store, or deployment environment variables. Do not commit keys to `appsettings*.json`.

```bash
AI_PROVIDER=OpenAI
OPENAI_API_KEY=<your-api-key>
OPENAI_MODEL=gpt-4.1-mini
OPENAI_BASE_URL=https://api.openai.com
```

OpenAI usage may require billing to be enabled on the OpenAI account, and API calls may incur cost. The backend will fail fast if `OpenAi:ApiKey` or `OpenAi:Model` is missing when the provider is `OpenAI`.

### Ollama Provider

Use `Ollama` for local model responses without calling an external AI provider. Install Ollama, start the local service, pull a model, and point MarketFlow at the local base URL.

```bash
ollama pull llama3.1
ollama serve
```

```bash
AI_PROVIDER=Ollama
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=llama3.1
dotnet run --project src/MarketFlow.Api/MarketFlow.Api.csproj
```

The configured `Ollama:Model` must match a model available locally. The backend posts to `Ollama:BaseUrl` plus `api/chat`; tests mock this HTTP call and do not require Ollama to be running.

### Safety And Tenant Isolation

- Tenant schema is resolved from authenticated backend user context, not from client-provided schema names.
- AI requests can include `marketId` and `departmentId`, but operational roles are constrained to their active staff assignment.
- `CompanyAdmin` can use company-wide tenant data. `MainOperator` is limited to the assigned market. `DepartmentManager` is limited to the assigned department. `InventoryEmployee` is limited to inventory AI features in the assigned market or department.
- Prompt payloads contain aggregated or calculated business data, not raw credentials, API keys, tokens, user records, private customer data, or cross-company data.
- Unsafe chat requests for SQL, database instructions, secrets, user emails, or other-company data are rejected with a safe response.
- Anomaly explanations sanitize accusatory wording such as fraud or theft and frame findings as items needing review.
- AI output is advisory. Backend calculations, authorization, tenant isolation, and persisted operational data remain controlled by application code.

### Limitations

- Model quality depends on the selected provider and model.
- `Fake` responses are deterministic and useful for development, but they are not real analysis.
- Ollama response speed depends on local hardware and model size.
- OpenAI requires network access and a valid key.
- AI services summarize and explain existing tenant data; they do not create purchases, change inventory, update suppliers, or bypass normal API permissions.

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
