# GitHub Actions: PRODUCTION Environment Deployment Guide

**Application:** Alpla Angola - Portal Gerencial  
**Target Server:** AOVIA1VMS011  
**Target Environment:** PRODUCTION  
**Official URL:** https://portalgerencial.alpla.net/  
**Workflow File:** [deploy-prod.yml](file:///c:/dev/alpla-portal/.github/workflows/deploy-prod.yml)

---

## 1. Purpose

This document describes the GitHub Actions CI/CD workflow for deploying the **Alpla Angola Portal Gerencial** to the **PRODUCTION** environment on the self-hosted Windows runner `AOVIA1VMS011`.

The workflow:
- Builds the backend (.NET 8) and frontend (React/Vite) on a GitHub-hosted runner
- Validates the target database is exactly `[Portal-Gerencial]`
- Creates a SQL database backup before deployment
- Deploys to the PRODUCTION server via a self-hosted runner
- Performs timestamped file backups before each deployment
- Preserves environment-specific configuration files
- Preserves the Production `web.config` (reverse proxy to port 5002)
- Stops/starts only PRODUCTION IIS App Pools
- Runs a smoke test against the Production API health endpoint
- Verifies Test environment integrity post-deployment

> **This workflow does NOT:**
> - Touch the Test environment, Test App Pools, or Test database
> - Use port 5000 (reserved) or port 5001 (Test)
> - Commit or expose any secrets
> - Drop or recreate the Production database
> - Run a separate migration step (migrations run on app startup)

---

## 2. Architecture

The Production and Test environments coexist on the same server (`AOVIA1VMS011`) with complete isolation:

| Aspect | TEST | PRODUCTION |
|:---|:---|:---|
| Web path | `C:\Apps\AlplaPortal\Test\web` | `C:\Apps\AlplaPortal\Prod\web` |
| API path | `C:\Apps\AlplaPortal\Test\api` | `C:\Apps\AlplaPortal\Prod\api` |
| API port | 5001 | **5002** |
| Database | `[Portal-Gerencial-Test]` | **`[Portal-Gerencial]`** |
| URL | `portalgerencial-test.alpla.net` | **`portalgerencial.alpla.net`** |
| ASP.NET env | `Test` | **`Production`** |
| IIS Web site | `AlplaPortal-Test-Web` | **`AlplaPortal-Prod-Web`** |
| IIS API site | `AlplaPortal-Test-Api` | **`AlplaPortal-Prod-Api`** |
| IIS Web pool | `AlplaPortal-Test-Web-Pool` | **`AlplaPortal-Prod-Web-Pool`** |
| IIS API pool | `AlplaPortal-Test-Api-Pool` | **`AlplaPortal-Prod-Api-Pool`** |
| Workflow | `deploy-test.yml` | **`deploy-prod.yml`** |
| GitHub env | `test` | **`production`** |

### Workflow Architecture

```
┌───────────────────────────────┐
│  Job 1: BUILD                 │
│  (runs-on: windows-latest)    │
│                               │
│  1. Checkout                  │
│  2. Setup .NET 8              │
│  3. dotnet restore            │
│  4. dotnet build (Release)    │
│  5. dotnet publish → artifact │
│  6. Setup Node.js 20          │
│  7. npm ci                    │
│  8. npx tsc --noEmit          │
│  9. npm run build             │
│  10. Upload API artifact      │
│  11. Upload Web artifact      │
└───────────────┬───────────────┘
                │
                ▼
┌───────────────────────────────┐
│  Job 2: DEPLOY PRODUCTION     │
│  (runs-on: self-hosted)       │
│  (environment: production)    │
│                               │
│  1. Download artifacts        │
│  2. Validate DB name          │
│  3. SQL database backup       │
│  4. Backup current files      │
│  5. Stop PROD App Pools       │
│  6. Deploy API files          │
│  7. Deploy Web files          │
│  8. Start PROD App Pools (*)  │
│  9. Smoke test                │
│  10. Verify Test integrity    │
│  11. Deployment summary       │
│                               │
│  (*) Runs even on failure     │
└───────────────────────────────┘
```

---

## 3. Prerequisites

### 3.1 Self-Hosted Runner

The GitHub Actions self-hosted runner on `AOVIA1VMS011` must have these labels:

| Label | Purpose |
|:---|:---|
| `self-hosted` | Standard self-hosted runner label |
| `Windows` | Operating system |
| `X64` | Architecture |
| `iis` | Indicates IIS management capability |

> **Future enhancement:** Add a dedicated `alpla-portal-prod` label to the runner for stricter targeting.

### 3.2 GitHub Environment

A GitHub Environment named **`production`** must be configured in the repository settings.

**Recommended protection rules:**
- Required reviewers (at least one approver before deployment)
- Deployment branch restrictions (only `main`)

### 3.3 GitHub Environment Variables

| Variable | Value | Description |
|:---|:---|:---|
| `API_DEPLOY_PATH` | `C:\Apps\AlplaPortal\Prod\api` | Server path for the API binaries |
| `WEB_DEPLOY_PATH` | `C:\Apps\AlplaPortal\Prod\web` | Server path for the frontend |
| `BACKUP_PATH` | `C:\Apps\AlplaPortal\Prod\backups` | Server path for backups |
| `IIS_API_APPPOOL` | `AlplaPortal-Prod-Api-Pool` | IIS Application Pool for the API |
| `IIS_WEB_APPPOOL` | `AlplaPortal-Prod-Web-Pool` | IIS Application Pool for the frontend |
| `PROD_API_HEALTH_URL` | `http://localhost:5002/health` | Health check endpoint URL |

### 3.4 GitHub Secrets

| Secret | Description |
|:---|:---|
| `PROD_DB_CONNECTION_STRING` | SQL Server connection string for `[Portal-Gerencial]` database. Used for pre-deploy validation and backup. |

### 3.5 Bootstrap Script

Before the first deployment, run the bootstrap script on `AOVIA1VMS011` as Administrator:

3. **Transfer the files to AOVIA1VMS011**
   - Copy `setup-production-environment.ps1` and `validate-production-environment.ps1` to the server (e.g. `C:\temp\deploy-scripts\`).
   - *Important:* Ensure the files are copied exactly as UTF-8. You can validate they didn't get corrupted during transfer by running this on the server:
     ```powershell
     $t=$null; $e=$null; [System.Management.Automation.Language.Parser]::ParseFile("setup-production-environment.ps1", [ref]$t, [ref]$e) | Out-Null; $e
     ```
     (This should return absolutely nothing if the file is intact).

4. **Run the bootstrap script**
   - Open PowerShell **as Administrator**.:

```powershell
cd C:\path\to\scripts\server
.\setup-production-environment.ps1
# Or with optional parameters:
.\setup-production-environment.ps1 -CertificateThumbprint "YOUR_THUMBPRINT"
```

See [setup-production-environment.ps1](file:///c:/dev/alpla-portal/scripts/server/setup-production-environment.ps1).

---

## 4. How to Run the Workflow

1. Go to the repository on GitHub.
2. Navigate to **Actions** → **Deploy to PRODUCTION**.
3. Click **Run workflow**.
4. Select branch: `main`.
5. Enter the **version** (e.g., `v2.185.0`).
6. Click **Run workflow**.
7. If environment protection is configured, **approve the deployment** when prompted.
8. Monitor the workflow run.

---

## 5. Configuration File Preservation

During API deployment, the following files are preserved (never overwritten):

- `appsettings.Production.json`
- `appsettings.Staging.json`
- `appsettings.Test.json`
- `appsettings.Local.json`

For the frontend, the **server-side `web.config`** is always preserved. This file contains the reverse proxy rule targeting **port 5002** (Production). The build artifact ships with port 5001 (Test) and must never overwrite the Production configuration.

---

## 6. Reverse Proxy (Port Routing)

The frontend `web.config` on the Production server routes API calls:

```
/api/*  →  http://localhost:5002/api/*
```

This is different from Test (`localhost:5001`). The same build artifact works for both environments — the port difference is entirely server-side.

**Critical:** The deploy workflow always preserves the server-side `web.config` during deployment. If it is accidentally deleted, it must be recreated using the bootstrap script or manually.

---

## 7. Database Migrations

Migrations run **automatically on application startup** via `Database.Migrate()` in `Program.cs`.

The Production workflow adds safety layers:

1. **Pre-deploy validation:** Confirms the connection string targets `[Portal-Gerencial]` (blocks if it resolves to `[Portal-Gerencial-Test]`)
2. **Pre-deploy backup:** Creates a compressed SQL backup to `C:\Apps\AlplaPortal\Prod\backups\db\`
3. **Startup migration:** The application applies pending EF Core migrations on startup
4. **Crash on failure:** In non-Development environments, the application crashes if migration fails (by design)
5. **Smoke test detection:** The workflow detects startup failure via the health check

---

## 8. Backup Strategy

### File Backups
```
C:\Apps\AlplaPortal\Prod\backups\
  └── backup_20260602_140000\
      ├── api\    (full copy of current API files)
      └── web\    (full copy of current Web files)
```

### Database Backups
```
C:\Apps\AlplaPortal\Prod\backups\
  └── db\
      └── Portal-Gerencial_20260602_140000.bak
```

---

## 9. `appsettings.Production.json`

This file must exist at `C:\Apps\AlplaPortal\Prod\api\appsettings.Production.json` with Production-specific overrides:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "[REDACTED — SQL Server connection string to Portal-Gerencial database]"
  },
  "Jwt": {
    "Secret": "[REDACTED — production-grade JWT signing key, minimum 32 characters]"
  },
  "AppConfig": {
    "FrontendBaseUrl": "https://portalgerencial.alpla.net",
    "UploadStoragePath": "C:\\Apps\\AlplaPortal\\Prod\\uploads",
    "LogsPath": "C:\\Apps\\AlplaPortal\\Prod\\logs",
    "TempPath": "C:\\Apps\\AlplaPortal\\Prod\\temp"
  }
}
```

> **IMPORTANT:** This file is preserved during deployment and never committed to the repository.

---

## 10. Troubleshooting

### Smoke test fails after deployment
- The API may need time to apply migrations on first startup.
- Check Event Viewer → Windows Logs → Application → Source: IIS AspNetCore Module.
- Check `C:\Apps\AlplaPortal\Prod\api\logs\` for startup logs.

### Frontend shows blank page
- Verify `C:\Apps\AlplaPortal\Prod\web\assets\` contains `.js` and `.css` files.
- Verify the `web.config` exists and has the SPA fallback rule.

### API returns 502 Bad Gateway
- Verify the Production API is running: `Get-WebAppPoolState -Name "AlplaPortal-Prod-Api-Pool"`
- Verify the API is listening on port 5002: `Get-NetTCPConnection -LocalPort 5002`
- Verify ARR proxy is enabled in IIS Manager.
- Check the `web.config` reverse proxy targets `localhost:5002`.

### Wrong database
- The workflow validates the database name before deployment.
- If `PROD_DB_CONNECTION_STRING` is not set, the validation is skipped.
- Always verify `appsettings.Production.json` points to `[Portal-Gerencial]`.

---

## 11. Security Notes

- **No secrets are stored in the workflow file or repository.**
- Connection strings, JWT secrets, and integration credentials are in `appsettings.Production.json` on the server.
- The workflow uses GitHub environment variables (non-sensitive paths) and one GitHub secret (`PROD_DB_CONNECTION_STRING`).
- The workflow preserves all `appsettings.*.json` files during deployment.
- The `web.config` is always preserved to prevent port misconfiguration.

---

## 12. Manual Steps Required from Leonardo

| # | Step | When |
|:---:|:---|:---|
| 1 | Run `setup-production-environment.ps1` on AOVIA1VMS011 as Admin | Before first deploy |
| 2 | Create GitHub environment `production` with required reviewers | Before first deploy |
| 3 | Configure GitHub environment variables (see §3.3) | Before first deploy |
| 4 | Configure GitHub secret `PROD_DB_CONNECTION_STRING` | Before first deploy |
| 5 | Create `[Portal-Gerencial]` database on SQL Server | Before first deploy |
| 6 | Create `appsettings.Production.json` on the server | Before first deploy |
| 7 | Confirm DNS for `portalgerencial.alpla.net` | Before first deploy |
| 8 | Install/confirm SSL certificate | Before first deploy |
| 9 | Provide certificate thumbprint to bootstrap script (if needed) | Before first deploy |
| 10 | Add `alpla-portal-prod` label to runner (optional, future) | When convenient |
| 11 | Seed admin user `leonardo.cintra@alpla.com` after first deploy | After first deploy |
