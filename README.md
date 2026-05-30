# MarketFlow Backend

MarketFlow is a multi-tenant ASP.NET Core API for supermarket and retail operations. It manages companies, users, markets, departments, products, inventory, purchases, suppliers, sales, dashboards, and AI-assisted business insights.

The backend is responsible for authentication, tenant resolution, role and permission checks, operational workflows, background jobs, deterministic business calculations, and optional AI text generation through Fake, OpenAI, or Ollama providers.

## Contents

- [Architecture](#architecture)
- [Multi-Tenancy](#multi-tenancy)
- [Authentication And Authorization](#authentication-and-authorization)
- [Setup](#setup)
- [Running The API](#running-the-api)
- [API Overview](#api-overview)
- [Application Flows](#application-flows)
- [AI Usage](#ai-usage)
- [Testing](#testing)
- [Project Structure](#project-structure)
- [Deployment Notes](#deployment-notes)

## Architecture

The solution follows a layered architecture:

| Layer | Project | Responsibilities |
| --- | --- | --- |
| API | `src/MarketFlow.Api` | Controllers, middleware, JWT/auth policy registration, CORS, OpenAPI, hosted services wiring, environment configuration. |
| Application | `src/MarketFlow.Application` | Feature services, DTOs, interfaces, business rules, validation, permission-aware workflows. |
| Domain | `src/MarketFlow.Domain` | Core entities, enums, and shared domain base types. |
| Infrastructure | `src/MarketFlow.Infrastructure` | EF Core persistence, PostgreSQL tenant queries, repositories, seeding, Redis cache, background jobs, AI clients, auth helpers. |
| Tests | `tests/MarketFlow.Api.Tests` | Unit, authorization, controller, background job, and PostgreSQL-backed integration coverage. |

High-level request flow:

1. HTTP requests enter `MarketFlow.Api` controllers.
2. Middleware handles request logging and exception formatting.
3. JWT authentication validates the token and rejects revoked access tokens.
4. Authorization policies check role, active-user state, permissions, and tenant assignment scope.
5. Application services execute use-case logic.
6. Infrastructure stores read or write global data in `public` tables and tenant data in the resolved tenant schema.
7. Responses are returned through a consistent `ServiceResult` style shape.

## Multi-Tenancy

MarketFlow uses PostgreSQL schema-based tenancy.

- Global platform tables live in the `public` schema. These include companies, users, roles, and revoked access tokens.
- Each company has a tenant schema, usually prefixed with `tenant_`, that stores operational data such as markets, departments, staff assignments, products, inventory, sales, purchases, suppliers, AI analysis requests, and AI results.
- `TenantProvider` resolves the current tenant from authenticated backend user context and persisted company data.
- Tenant schema names are not trusted from client input or JWT claims.
- `TenantQueryService` centralizes tenant-aware reads and writes, applies role assignment scope, and protects cross-company isolation.

Tenant scope by role:

| Role | Tenant Scope |
| --- | --- |
| `RootAdmin` | Platform/global operations only. No tenant inventory, sales, or AI data access by default. |
| `CompanyAdmin` | Full access inside the user's company tenant. |
| `MainOperator` | Assigned market inside the company tenant. |
| `DepartmentManager` | Assigned department inside an assigned market. |
| `InventoryEmployee` | Assigned market or assigned department for inventory workflows. |
| `Seller` | Assigned POS market availability and sale creation workflows. |

Operational users (`MainOperator`, `DepartmentManager`, `InventoryEmployee`, and `Seller`) must be created with a `marketId`. They may also include a `departmentId`. `CompanyAdmin` users should omit both fields.

## Authentication And Authorization

Authentication uses JWT bearer tokens plus refresh tokens.

Important endpoints:

- `POST /api/auth/login` authenticates a user and returns access and refresh tokens.
- `POST /api/auth/refresh-token` issues a new access token from a valid refresh token.
- `POST /api/auth/logout` revokes the current access token.
- `GET /api/auth/me` returns the authenticated user profile and assignment.
- `GET /api/profile`, `PUT /api/profile`, and `PUT /api/profile/password` support self-service profile management for active users.

Authorization is policy-based. Policies combine:

- A valid authenticated user.
- Active user and active company checks.
- ASP.NET role checks such as `RootAdmin`, `CompanyAdmin`, or `MainOperator`.
- JSON permission keys stored on seeded roles.
- Tenant assignment scope for market and department constrained workflows.

Seeded roles and main permissions:

| Role | Main Permissions |
| --- | --- |
| `RootAdmin` | Platform company management and root/admin user management. |
| `CompanyAdmin` | Company-wide users, markets, departments, products, sales, inventory, purchases, suppliers, and AI features. |
| `MainOperator` | Assigned-market operations: products, sales, inventory, purchases, supplier reads, and limited user management. |
| `DepartmentManager` | Department-scoped products, sales reads, inventory reads, stock updates, adjustments, and transfers. |
| `InventoryEmployee` | Inventory reads, stock updates, stock adjustments, and movement history in assigned scope. |
| `Seller` | POS product availability and sale creation in assigned scope. |

Common permission keys include `users:read`, `users:create`, `products:read`, `products:create`, `sales:create`, `inventory:read`, `stock:update`, `stock:adjust`, `stock:transfer`, `purchases:read`, and `suppliers:read`.

## Setup

### Prerequisites

- .NET SDK compatible with `net10.0`.
- PostgreSQL.
- Redis, used by cache-backed infrastructure such as AI result caching and barcode lookup caching.
- Optional: Ollama for local AI model responses.
- Optional: OpenAI API key for hosted AI responses.
- Optional: `dotnet-ef` for applying EF Core migrations from the command line.

### Local Configuration

Copy the example environment file and fill in local values:

```bash
cp .env.example .env
```

Do not commit secrets. Keep real credentials in `.env`, user secrets, or deployment environment variables. The API loads `.env` at startup and maps friendly variables into .NET configuration keys.

Minimum local values:

```bash
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5000

DB_HOST=localhost
DB_PORT=5432
DB_NAME=marketflow_db
DB_USERNAME=postgres
DB_PASSWORD=<postgres-password>

JWT_SECRET=<long-random-secret>
JWT_ISSUER=MarketFlow
JWT_AUDIENCE=MarketFlowUsers

ROOT_ADMIN_EMAIL=admin@marketflow.com
ROOT_ADMIN_PASSWORD=<initial-root-admin-password>

TENANT_SCHEMA_PREFIX=tenant_
REDIS_CONNECTION=localhost:6379
AI_PROVIDER=Fake
FRONTEND_URL=http://localhost:5173
```

You can also use `DB_CONNECTION_STRING` instead of individual `DB_*` values.

### Database

Create the local database, then apply migrations:

```bash
createdb marketflow_db
dotnet ef database update --project src/MarketFlow.Infrastructure --startup-project src/MarketFlow.Api
```

If `dotnet ef` is not available, install it with:

```bash
dotnet tool install --global dotnet-ef
```

In `Development`, startup seeds:

- Default roles and permission JSON.
- A platform company with schema `platform_admin`.
- The initial root admin user.
- Default tenant categories for active companies when tenant schemas are available.

## Running The API

Run the API:

```bash
dotnet run --project src/MarketFlow.Api/MarketFlow.Api.csproj
```

Development OpenAPI endpoints:

- Swagger UI: `http://localhost:5000/swagger`
- OpenAPI JSON: `http://localhost:5000/openapi/v1.json`
- Database health check: `GET /api/health/db`

The API enables HTTPS redirection, CORS for `FRONTEND_URL`, JWT authentication, and authorization policies through the API pipeline.

## API Overview

All controller routes are under `/api`.

| Module | Main Routes | Purpose |
| --- | --- | --- |
| Auth | `/api/auth/*` | Register/login, root admin creation, refresh token, logout, current user. |
| Companies | `/api/companies` | Root-admin company onboarding and company lookup. |
| Users | `/api/users` | User listing, creation, updates, activation/deactivation, and assignment summaries. |
| Profile | `/api/profile` | Current user profile and password updates. |
| Markets | `/api/markets` | Tenant market CRUD and active-state management. |
| Departments | `/api/departments` | Tenant department CRUD and active-state management. |
| Categories | `/api/categories` | Product category lookup. |
| Products | `/api/products` | Product CRUD, partial updates, soft deactivation/reactivation. |
| Inventory | `/api/inventory` | Stock records, low-stock view, POS product availability, stock movements, adjustments, transfers. |
| Purchases | `/api/purchases` | Purchase orders, updates, receiving, cancellation, deletion. |
| Suppliers | `/api/suppliers` | Supplier CRUD, status updates, deactivation. |
| Sales | `/api/sales` | Sales creation, history, details, updates, deletion. |
| Dashboard | `/api/dashboard/sales-summary` | Scoped dashboard sales KPIs. |
| AI | `/api/ai/*` | Chat, report queries, dashboard summaries, forecasts, recommendations, supplier insights, anomaly detection. |
| Health | `/api/health/db` | Database connectivity check. |

### Products API

Product removal is a soft-deactivation flow. `DELETE /api/products/{id}` remains supported, but it marks the product inactive instead of deleting the row. New clients should prefer:

- `POST /api/products/{id}/deactivate`
- `POST /api/products/{id}/reactivate`

Default product list and detail responses return active products. List callers can include inactive data with `includeInactive=true`, or request only inactive products with `isActive=false`. Generic `PUT` and `PATCH` product updates do not change `IsActive`.

### Inventory Authorization

The backend inventory matrix is defined in `InventoryPermissionMatrix` and uses these permission keys for API policies, seeded role permissions, and frontend route-guard parity:

| Role | Scope | View inventory | Create records | Update stock | Adjust stock | Delete records | View movements | Transfer stock |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `RootAdmin` | No tenant inventory access | No | No | No | No | No | No | No |
| `CompanyAdmin` | All company inventory | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| `MainOperator` | Assigned market inventory | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| `DepartmentManager` | Assigned department inventory | Yes | No | Yes | Yes | No | Yes | Yes |
| `InventoryEmployee` | Assigned market or department inventory | Yes | No | Yes | Yes | No | Yes | No |
| `Seller` | Assigned POS market availability only | Yes | No | No | No | No | No | No |

Permission keys:

- `inventory:read` gates inventory visibility and POS availability reads.
- `inventory:create` gates creating inventory records.
- `stock:update` gates full stock updates, including `PUT /api/inventory/{id}`.
- `stock:adjust` gates stock adjustments, including `PATCH /api/inventory/{id}` and `POST /api/inventory/{id}/adjust`.
- `inventory:delete` gates deleting inventory records.
- `inventory-movements:read` gates inventory movement history.
- `stock:transfer` gates transfers between allowed inventory scopes.

### Dashboard API

`GET /api/dashboard/sales-summary` returns the current user's scoped sales totals:

- `totalRevenue`
- `totalSales`
- `totalItemsSold`
- `averageSaleAmount`

Optional `from` and `to` query parameters filter by inclusive sale date, for example:

```http
GET /api/dashboard/sales-summary?from=2026-05-01&to=2026-05-31
```

The endpoint uses the same `sales:read` authorization policy, tenant schema resolution, and role assignment scoping as the sales endpoints.

## Application Flows

### Platform Onboarding

1. Apply migrations and run the API in `Development`.
2. The global seeder creates roles, the `platform_admin` company, and the first root admin.
3. Root admin logs in through `/api/auth/login`.
4. Root admin creates a company through `/api/companies`.
5. Company creation provisions the company tenant schema and creates the initial `CompanyAdmin`.
6. The `CompanyAdmin` logs in and manages tenant setup.

### Tenant Setup

1. `CompanyAdmin` creates markets.
2. `CompanyAdmin` creates departments under markets.
3. `CompanyAdmin` creates operational users with market and optional department assignments.
4. Product categories are seeded; tenant products are created under categories.
5. Inventory records are created for products, markets, and departments.
6. Suppliers and purchases are added as procurement begins.

### Daily Operations

1. Sellers use POS product availability and create sales.
2. Sales update operational totals and inventory availability.
3. Inventory users update, adjust, and transfer stock based on role scope.
4. Purchases move through creation, update, receiving, cancellation, and deletion flows.
5. Dashboard and report endpoints surface scoped KPIs to managers.
6. Background jobs create low-stock alerts and AI analysis results when enabled.

### User Responses And Assignments

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

Assignment summaries are read from each company's tenant schema. If a tenant schema is temporarily missing assignment tables or lacks read privileges, user list responses still return global user records and omit assignment summaries for the affected schema while the backend logs a warning. User creation still requires a writable tenant `staff_assignments` table so assignment insert failures roll back the created user.

## AI Usage

MarketFlow AI features are backend-only. The frontend calls MarketFlow `/api/ai/*` endpoints and never calls OpenAI or Ollama directly. The backend resolves the current tenant, loads tenant-safe business data, performs deterministic calculations in application services, and sends only scoped summary data to the configured `IAiClient` provider for wording, explanations, or summaries.

### AI Architecture

- `AiController` exposes AI endpoints and enforces endpoint policies.
- `AiTenantScopeAuthorizationFilter` applies tenant and assignment scope before AI services run.
- Application services calculate forecasts, recommendations, supplier reliability, and anomalies from backend data first.
- `FakeAiClient` returns deterministic local JSON/text for development and tests.
- `OpenAiClient` calls OpenAI chat completions.
- `OllamaAiClient` calls a local Ollama `/api/chat` server.
- AI responses are advisory text. The backend does not let the model change calculated quantities, stock levels, supplier metrics, anomaly thresholds, authorization, or tenant scope.

### AI Endpoints

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

### AI Provider Configuration

Provider values are configured with `Ai:Provider` or `AI_PROVIDER`. Supported values are `Fake`, `OpenAI`, and `Ollama`.

```bash
AI_PROVIDER=Fake
OPENAI_API_KEY=
OPENAI_MODEL=gpt-4.1-mini
OPENAI_BASE_URL=https://api.openai.com
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=llama3.1
```

Use `Fake` for local development, automated tests, CI, and demos where deterministic responses are preferred.

Use `OpenAI` when real hosted model responses are needed:

```bash
AI_PROVIDER=OpenAI
OPENAI_API_KEY=<your-api-key>
OPENAI_MODEL=gpt-4.1-mini
OPENAI_BASE_URL=https://api.openai.com
```

Use `Ollama` for local model responses:

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

### AI Safety And Tenant Isolation

- Tenant schema is resolved from authenticated backend user context, not client-provided schema names.
- AI requests can include `marketId` and `departmentId`, but operational roles are constrained to their active staff assignment.
- `CompanyAdmin` can use company-wide tenant data. `MainOperator` is limited to the assigned market. `DepartmentManager` is limited to the assigned department. `InventoryEmployee` is limited to inventory AI features in the assigned market or department.
- Prompt payloads contain aggregated or calculated business data, not raw credentials, API keys, tokens, user records, private customer data, or cross-company data.
- Unsafe chat requests for SQL, database instructions, secrets, user emails, or other-company data are rejected with a safe response.
- Anomaly explanations sanitize accusatory wording and frame findings as items needing review.
- AI output is advisory. Backend calculations, authorization, tenant isolation, and persisted operational data remain controlled by application code.

## Testing

Run the test suite:

```bash
dotnet test
```

Run the API test project directly:

```bash
dotnet test tests/MarketFlow.Api.Tests/MarketFlow.Api.Tests.csproj
```

PostgreSQL-backed integration tests are guarded by custom xUnit attributes and are skipped unless `MARKETFLOW_TEST_DB_CONNECTION_STRING` is configured.

```bash
MARKETFLOW_TEST_DB_CONNECTION_STRING="Host=localhost;Port=5432;Database=marketflow_test;Username=postgres;Password=postgres" \
dotnet test tests/MarketFlow.Api.Tests/MarketFlow.Api.Tests.csproj
```

Use a disposable test database. Integration test utilities create and drop tenant schemas and assume the connection can create schemas, tables, functions, triggers, and indexes.

Useful coverage areas in `tests/MarketFlow.Api.Tests`:

- `AI` covers AI calculations, prompt safety, fake client behavior, and model-output guardrails.
- `Authorization` covers policy registration and role/permission matrices.
- `Integration` covers tenant schema provisioning, tenant isolation, scoped inventory/sales behavior, purchase receipt flows, and endpoint authorization.
- `BackgroundJobs` covers low-stock alerts and AI analysis jobs.
- Feature folders such as `Products`, `Users`, `Sales`, `Suppliers`, and `Dashboard` cover service and controller behavior.

## Project Structure

```text
MarketFlow.sln
README.md
.env.example
src/
  MarketFlow.Api/
    Authorization/        Policy requirements, handlers, AI tenant scope filter.
    Configuration/        .env loading and friendly environment variable mapping.
    Controllers/          HTTP API controllers.
    Extensions/           Service registration and request pipeline setup.
    Middleware/           Exception handling and request logging.
    Services/             API-hosted services and current-user adapter.
  MarketFlow.Application/
    Common/               Shared application interfaces and result types.
    Features/             Business modules grouped by feature.
  MarketFlow.Domain/
    Common/               Shared domain primitives.
    Entities/             Global and tenant domain entities.
    Enums/                Domain enumerations.
  MarketFlow.Infrastructure/
    BackgroundJobs/       Stock alert and AI analysis jobs.
    Caching/              Redis cache services.
    MultiTenancy/         Tenant provider and access exceptions.
    OpenAI/               Fake, OpenAI, and Ollama AI clients and options.
    Persistence/          EF Core context, migrations, configurations, tenant query service.
    Repositories/         Data access helpers.
    Seed/                 Development/global data seeding.
    Services/             Infrastructure services such as auth helpers.
tests/
  MarketFlow.Api.Tests/   Unit, controller, authorization, integration, and background job tests.
```

## Deployment Notes

- Require HTTPS for deployed API traffic.
- Do not store secrets in tracked files such as `appsettings.json`, `appsettings.Development.json`, or `launchSettings.json`.
- Configure `JWT_SECRET`, database credentials, Redis connection, CORS frontend URLs, and AI provider settings through deployment secrets.
- Company onboarding receives the initial `CompanyAdmin` password in plaintext over the request body before the backend hashes it with BCrypt, so TLS is mandatory.
- For production deployments with multi-region or cross-database tenancy, verify that the API role has read/write access to tenant schemas and acceptable latency for per-schema assignment summary lookups.
- For companies with large user counts, monitor the user list assignment lookup query against `staff_assignments`. The backend batches assignment summary lookups per tenant schema in groups of 1,000 users and uses the existing `idx_staff_user` index.
