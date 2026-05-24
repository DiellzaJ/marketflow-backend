# MarketFlow

MarketFlow is a multi-tenant market management system for retail companies. It helps companies manage users, markets, departments, inventory, purchases, sales/POS checkout, sales history, reports, and low-stock tracking from one centralized application.

The project uses an ASP.NET Core backend, a React + TypeScript frontend, and PostgreSQL schema-based multi-tenancy so each company has isolated operational data.

## Project Members

| # | Name |
| --- | --- |
| 1 | Dren Morina |
| 2 | Riga Ferati |
| 3 | Djellza Jasiqi |
| 4 | Dituri Kodra |
| 5 | Nora Morina |

## Architecture Overview

| Layer | Technology | Purpose |
| --- | --- | --- |
| Backend | ASP.NET Core | REST API, authentication, authorization, business logic, and tenant-aware data access. |
| Frontend | React, TypeScript, Vite | User interface for administration, inventory, POS, sales history, and reports. |
| Database | PostgreSQL | Global data and schema-based tenant data isolation. |
| Testing | xUnit, Vitest | Backend and frontend automated testing. |

### Multi-Tenancy

MarketFlow uses PostgreSQL schemas to isolate company data:

- Shared system data is stored in the `public` schema.
- Each company has its own tenant schema.
- Operational data such as markets, departments, inventory, purchases, and sales is stored inside the tenant schema.

### Authentication

Users log in through the API and receive a JWT access token. The frontend sends this token with secured requests using the `Authorization: Bearer` header. The backend validates the token and applies role-based permissions before returning data.

## Main Modules

| Module | Description |
| --- | --- |
| Authentication and Authorization | Login, JWT authentication, and role-based permissions. |
| Companies | Company onboarding and tenant creation. |
| Users | User management, roles, activation state, and staff assignments. |
| Markets | Market creation and management. |
| Departments | Department management inside markets. |
| Inventory | Stock records, quantities, adjustments, transfers, and availability. |
| Inventory Movements | History of stock changes from purchases, sales, transfers, and adjustments. |
| Purchases | Purchase management and stock receiving. |
| Sales/POS | Checkout flow, sale creation, sale items, totals, and payment method. |
| Sales History | Paginated sales list with filtering and sorting for reporting pages. |
| Reports | Reporting data for operational analysis and dashboards. |
| Low-Stock Support | Low-stock monitoring and alert support. |

## Roles and Access

| Role | Access Overview |
| --- | --- |
| `RootAdmin` | Platform administration and company onboarding. No tenant inventory or sales access by default. |
| `CompanyAdmin` | Full access inside the company tenant. |
| `MainOperator` | Access to assigned market operations. |
| `DepartmentManager` | Access to assigned department inventory and sales data. |
| `InventoryEmployee` | Inventory-focused access for assigned market or department. |
| `Seller` | POS-focused access for creating sales and viewing checkout availability. |

Access is controlled by both role permissions and staff assignment. For example, a `MainOperator` sees data for the assigned market, while a `DepartmentManager` sees data for the assigned department.

## Technologies Used

| Area | Technologies |
| --- | --- |
| Backend | ASP.NET Core, Entity Framework Core, PostgreSQL, JWT Bearer authentication |
| Frontend | React, TypeScript, Vite, Vitest |
| Tools | GitHub, GitHub Projects, DBeaver, .NET SDK, npm |

## API Overview

Main API groups:

| Endpoint Group | Purpose |
| --- | --- |
| `/api/auth` | Authentication and token handling. |
| `/api/users` | User and staff assignment management. |
| `/api/products` | Product catalog management. |
| `/api/inventory` | Inventory, stock operations, low-stock data, and inventory movements. |
| `/api/purchases` | Purchase and receiving workflows. |
| `/api/sales` | Sales, POS checkout, sale details, and sales history. |
| `/api/reports` | Reporting data for dashboards and analytics. |

Secured requests use:

```http
Authorization: Bearer eyJhbGciOi...
```

## Backend Setup

### Prerequisites

- .NET SDK 10.0 or compatible newer SDK
- PostgreSQL
- Optional Redis for cache-backed features

### Restore Packages

```bash
dotnet restore MarketFlow.sln
```

### Configure Environment

Copy the example environment file:

```bash
cp .env.example .env
```

Required configuration:

| Key | Purpose |
| --- | --- |
| `DB_CONNECTION_STRING` or `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USERNAME`, `DB_PASSWORD` | PostgreSQL connection configuration. |
| `JWT_SECRET` | Secret used to sign JWT tokens. |
| `JWT_ISSUER` | JWT issuer. |
| `JWT_AUDIENCE` | JWT audience. |
| `ROOT_ADMIN_FULL_NAME` | Seeded root administrator name. |
| `ROOT_ADMIN_EMAIL` | Seeded root administrator email. |
| `ROOT_ADMIN_PASSWORD` | Seeded root administrator password. |
| `TENANT_SCHEMA_PREFIX` | Prefix for tenant schemas. |
| `FRONTEND_URL` | Allowed frontend URL for CORS. |

Optional configuration:

| Key | Purpose |
| --- | --- |
| `REDIS_CONNECTION` | Redis connection string. |
| `OPENAI_API_KEY` | API key for future AI assistant integration. |
| `OPENAI_MODEL` | OpenAI model configuration. |

### Apply Migrations

```bash
dotnet ef database update \
  --project src/MarketFlow.Infrastructure/MarketFlow.Infrastructure.csproj \
  --startup-project src/MarketFlow.Api/MarketFlow.Api.csproj
```

### Run Backend

```bash
dotnet run --project src/MarketFlow.Api/MarketFlow.Api.csproj
```

Default development API URL:

```text
http://localhost:5000
```

## Frontend Setup

Run these commands from the frontend project directory.

### Install Packages

```bash
npm install
```

### Run Frontend

```bash
npm run dev
```

Default Vite URL:

```text
http://localhost:5173
```

The frontend should call the backend API at:

```text
http://localhost:5000
```

## Application Usage Flow

1. `RootAdmin` logs in.
2. `RootAdmin` creates a company.
3. The system creates the company's tenant schema.
4. `CompanyAdmin` logs in and creates markets and departments.
5. `CompanyAdmin` creates users and assigns staff to markets or departments.
6. Inventory records are created and updated.
7. Purchases are created and received into stock.
8. Sellers use the POS checkout flow to create sales.
9. Managers review inventory movements, sales history, low-stock data, and reports.

## Testing

### Backend

```bash
dotnet test tests/MarketFlow.Api.Tests/MarketFlow.Api.Tests.csproj
```

Run all solution tests:

```bash
dotnet test MarketFlow.sln
```

### Frontend

```bash
npm run test
npm run lint
npm run build
```

## Project Structure

```text
marketflow-backend/
|-- MarketFlow.sln
|-- src/
|   |-- MarketFlow.Api/
|   |-- MarketFlow.Application/
|   |-- MarketFlow.Infrastructure/
|   `-- MarketFlow.Domain/
`-- tests/
    `-- MarketFlow.Api.Tests/
```

```text
marketflow-frontend/
|-- src/
|   |-- api/
|   |-- components/
|   |-- features/
|   |-- pages/
|   |-- routes/
|   `-- types/
|-- package.json
`-- vite.config.ts
```

## Future Improvements

- More advanced reports and analytics.
- Export reports to CSV, Excel, and PDF.
- Redis caching for faster lookups and reporting queries.
- OpenAI/AI assistant support for smart insights.
- Dashboard pages with charts, KPIs, and trends.

## Security Notes

- Use HTTPS in deployed environments.
- Keep secrets outside source control.
- Store production credentials in environment variables or a secure secret manager.
- Tenant data should always be accessed through authenticated and scoped API requests.
