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
