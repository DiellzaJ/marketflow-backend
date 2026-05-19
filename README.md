# marketflow-backend

## Configuration

Copy `.env.example` to `.env` for local development and keep real credentials in `.env` or your deployment environment variables.

Do not store secrets in tracked files such as `appsettings.json`, `appsettings.Development.json`, or `launchSettings.json`.

## Deployment Notes

Require HTTPS for API traffic in deployed environments. Company onboarding receives the initial CompanyAdmin password in plaintext over the request body before the backend hashes it with BCrypt.
