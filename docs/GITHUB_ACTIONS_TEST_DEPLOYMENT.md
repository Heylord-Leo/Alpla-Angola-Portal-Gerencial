# GitHub Actions: TEST Environment Deployment Guide

**Application:** Alpla Angola - Portal Gerencial  
**Target Server:** AOVIA1VMS011  
**Target Environment:** TEST  
**Official URL:** https://portalgerencial-test.alpla.net/  
**Workflow File:** [deploy-test.yml](file:///c:/dev/alpla-portal/.github/workflows/deploy-test.yml)

---

## 1. Purpose

This document describes the first GitHub Actions CI/CD workflow for deploying the **Alpla Angola Portal Gerencial** to the **TEST** environment on the self-hosted Windows runner `AOVIA1VMS011`.

The workflow:
- Builds the backend (.NET 8) and frontend (React/Vite) on a GitHub-hosted runner
- Deploys to the TEST server via a self-hosted runner
- Performs timestamped backups before each deployment
- Preserves environment-specific configuration files
- Stops/starts IIS App Pools safely
- Runs a smoke test against the API health endpoint

> **This workflow does NOT:**
> - Touch Production in any way
> - Automate EF Core database migrations
> - Commit or expose any secrets
> - Use port 5000 (reserved by another application on AOVIA1VMS011)

---

## 2. Prerequisites

### 2.1 Self-Hosted Runner

A GitHub Actions self-hosted runner must be installed and registered on `AOVIA1VMS011` with the following labels:

| Label | Purpose |
|:---|:---|
| `self-hosted` | Standard self-hosted runner label |
| `Windows` | Operating system |
| `X64` | Architecture |
| `iis` | Indicates IIS management capability |
| `test` | Environment tier |
| `alpla-portal-test` | Application-specific label |

### 2.2 GitHub Environment

A GitHub Environment named **`test`** must be configured in the repository settings.

### 2.3 GitHub Environment Variables

The following variables must be configured in the `test` environment:

| Variable | Value | Description |
|:---|:---|:---|
| `API_DEPLOY_PATH` | `C:\Apps\AlplaPortal\Test\api` | Server path for the API binaries |
| `WEB_DEPLOY_PATH` | `C:\Apps\AlplaPortal\Test\web` | Server path for the frontend static files |
| `BACKUP_PATH` | `C:\Apps\AlplaPortal\Test\backups` | Server path for timestamped backups |
| `IIS_API_APPPOOL` | `AlplaPortal-Test-Api-Pool` | IIS Application Pool for the API |
| `IIS_WEB_APPPOOL` | `AlplaPortal-Test-Web-Pool` | IIS Application Pool for the frontend |
| `TEST_API_HEALTH_URL` | `http://localhost:5001/swagger/index.html` | Health check endpoint URL |

### 2.4 IIS Configuration

#### IIS Sites

| Site | Hostname | Bindings |
|:---|:---|:---|
| `AlplaPortal-Test-Web` | `portalgerencial-test.alpla.net` | `http *:80:portalgerencial-test.alpla.net` / `https *:443:portalgerencial-test.alpla.net` |
| `AlplaPortal-Test-Api` | — | `http *:5001:` |

#### IIS Application Pools

| App Pool | Type |
|:---|:---|
| `AlplaPortal-Test-Web-Pool` | Frontend (static files) |
| `AlplaPortal-Test-Api-Pool` | Backend (.NET 8 API) |

> **IMPORTANT:** The TEST API uses **port 5001**. Port 5000 is reserved by another application on AOVIA1VMS011 and must **never** be used.

#### Required Server Folders

| Path | Purpose |
|:---|:---|
| `C:\Apps\AlplaPortal\Test\api` | API deployment target |
| `C:\Apps\AlplaPortal\Test\web` | Web deployment target |
| `C:\Apps\AlplaPortal\Test\backups` | Deployment backups |
| `C:\ActionsArtifacts\AlplaPortal\api` | Temporary artifact staging (API) |
| `C:\ActionsArtifacts\AlplaPortal\web` | Temporary artifact staging (Web) |

### 2.5 HTTPS Certificate

| Property | Value |
|:---|:---|
| **Subject** | `CN=portalgerencial-test.alpla.net` |
| **Thumbprint** | `057DEA6245C5EA9EE309E6B9367E649BB387B826` |
| **Status** | Installed and bound to the Web site on port 443 |

The SSL certificate must be installed in the Local Machine certificate store and correctly bound to the `AlplaPortal-Test-Web` IIS site for HTTPS access.

---

## 3. How to Run the Workflow

1. Go to the repository on GitHub.
2. Navigate to **Actions** → **Deploy to TEST**.
3. Click **Run workflow**.
4. Enter the **version** (e.g., `v2.154.0`).
5. Click **Run workflow** to start.

The workflow runs in two phases:
1. **Build** — Compiles backend and frontend on a GitHub-hosted Windows runner.
2. **Deploy TEST** — Downloads artifacts on the self-hosted runner, creates backups, deploys, and runs smoke tests.

---

## 4. Workflow Architecture

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
│  Job 2: DEPLOY TEST           │
│  (runs-on: self-hosted)       │
│  (environment: test)          │
│                               │
│  1. Download artifacts        │
│  2. Backup current files      │
│  3. Stop IIS App Pools        │
│  4. Deploy API files          │
│  5. Deploy Web files          │
│  6. Start IIS App Pools (*)   │
│  7. Smoke test                │
│  8. Deployment summary        │
│                               │
│  (*) Runs even on failure     │
└───────────────────────────────┘
```

---

## 5. Configuration File Preservation

During API deployment, the following environment-specific configuration files are **preserved** (not overwritten by the build artifact):

- `appsettings.Staging.json`
- `appsettings.Test.json`
- `appsettings.Production.json`
- `appsettings.Local.json`

These files contain server-side secrets (connection strings, JWT keys, integration credentials) that are configured directly on the server and must never be overwritten by CI/CD.

For the frontend, the server-side `web.config` (containing IIS SPA rewrite rules) is preserved if the build artifact does not include one.

The published `web.config` for the API (generated by `dotnet publish`) is always deployed as it is required by the IIS ASP.NET Core Module for hosting.

---

## 6. Backup Strategy

Before every deployment, the workflow creates a timestamped backup:

```
C:\Apps\AlplaPortal\Test\backups\
  └── backup_20260526_143000\
      ├── api\    (full copy of current API files)
      └── web\    (full copy of current Web files)
```

To manually rollback:
1. Stop both IIS App Pools
2. Copy the contents of the desired backup folder back to the deploy paths
3. Start both IIS App Pools
4. Verify the application

---

## 7. Database Migrations

> **EF Core database migrations are intentionally NOT automated in this workflow.**

Migrations are a critical operation that can cause data loss or schema corruption if executed incorrectly. For this reason, they are handled in a separate controlled phase:

1. **Before deployment:** If the release includes schema changes, the DBA or administrator must review the migration SQL.
2. **Manual execution:** Migrations are run manually using `sqlcmd` or SSMS with Windows Authentication against the `[Portal-Gerencial-Test]` database.
3. **After deployment:** The application is verified to ensure compatibility with the current database schema.

This approach ensures:
- Full visibility into what schema changes are applied
- Ability to review and test migrations independently
- Controlled rollback procedures
- No accidental schema changes to production

A future workflow enhancement may automate migrations with proper safeguards (dry-run, approval gates, rollback scripts).

---

## 8. Post-Deployment Validation Checklist

After the workflow completes successfully, validate:

| # | Check | How |
|:---:|:---|:---|
| 1 | **Smoke test passed** | Verify the workflow smoke test step shows HTTP 200 |
| 2 | **Web loads** | Open `https://portalgerencial-test.alpla.net/` in browser |
| 3 | **API responds** | Open `http://localhost:5001/swagger/index.html` from the server |
| 4 | **Login works** | Log in with a test account |
| 5 | **Dashboard loads** | Verify KPI cards render (Primavera integration) |
| 6 | **Port 5000 unused** | Confirm no listener on port 5000: `Get-NetTCPConnection -LocalPort 5000` |
| 7 | **Logs written** | Check API log files in the configured logs directory |
| 8 | **No production impact** | Confirm production site and database are untouched |

---

## 9. Same-Origin API Routing (Reverse Proxy)

The frontend and API are hosted on **separate IIS sites**:

| Component | IIS Site | URL |
|:---|:---|:---|
| Frontend | `AlplaPortal-Test-Web` | `https://portalgerencial-test.alpla.net/` |
| API | `AlplaPortal-Test-Api` | `http://localhost:5001/` (internal only) |

The browser cannot call `localhost:5001` directly. The frontend makes same-origin API calls to:
```
https://portalgerencial-test.alpla.net/api/auth/login
```

The frontend IIS site uses a `web.config` with URL Rewrite rules to reverse-proxy `/api/*` to the internal API:
```
/api/*  →  http://localhost:5001/api/*
```

### 9.1 Server Prerequisites for Reverse Proxy

The following must be installed on `AOVIA1VMS011` for the reverse proxy to work:

| Prerequisite | Purpose | Verification |
|:---|:---|:---|
| **IIS URL Rewrite Module v2.1** | Enables URL rewrite rules in `web.config` | Check: `Get-WebGlobalModule \| Where-Object { $_.Name -like '*Rewrite*' }` |
| **IIS Application Request Routing (ARR) 3.0** | Enables reverse proxy functionality | Check: IIS Manager → Server level → Application Request Routing Cache |
| **ARR Proxy Enabled** | Activates the proxy feature | IIS Manager → Server → Application Request Routing Cache → Server Proxy Settings → ✅ Enable proxy |

> **If ARR is not installed:**
> 1. Download the ARR 3.0 standalone installer from Microsoft.
> 2. Install on `AOVIA1VMS011`.
> 3. Open IIS Manager → Server level → Application Request Routing Cache → Server Proxy Settings.
> 4. Check **"Enable proxy"** → Apply.
> 5. Run `iisreset` to apply changes.

### 9.2 Frontend web.config

The file `src/frontend/public/web.config` is included in the Vite build output and deployed automatically. It contains:

1. **Reverse Proxy Rule**: `/api/*` → `http://localhost:5001/api/*`
2. **SPA Fallback Rule**: Non-file requests → `index.html` (React Router support)

Port 5000 is **never** used. The reverse proxy always targets port 5001.

---

## 10. Backend Environment Configuration

### 10.1 ASPNETCORE_ENVIRONMENT

The `ASPNETCORE_ENVIRONMENT` variable must be set to `Test` for the TEST API App Pool:

**How to set (IIS App Pool environment variable):**
```powershell
# Run on AOVIA1VMS011 as administrator:
Import-Module WebAdministration
$pool = "AlplaPortal-Test-Api-Pool"

# Get current environment variables collection
$envVars = Get-ItemProperty "IIS:\AppPools\$pool" -Name environmentVariables
# Add ASPNETCORE_ENVIRONMENT=Test
$envVar = New-Object Microsoft.Web.Administration.ConfigurationElement
# Manual method via IIS Manager:
# 1. Open IIS Manager → Application Pools → AlplaPortal-Test-Api-Pool → Advanced Settings
# 2. Environment Variables → Add: Name=ASPNETCORE_ENVIRONMENT, Value=Test
# 3. Restart the App Pool
```

Or edit via `applicationHost.config`:
```xml
<add name="AlplaPortal-Test-Api-Pool">
  <environmentVariables>
    <add name="ASPNETCORE_ENVIRONMENT" value="Test" />
  </environmentVariables>
</add>
```

### 10.2 appsettings.Test.json

The file `C:\Apps\AlplaPortal\Test\api\appsettings.Test.json` must exist on the server with environment-specific overrides. This file is **never committed** to the repository.

Required contents (minimum):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "[REDACTED — SQL Server connection string to Portal-Gerencial-Test database]"
  },
  "Jwt": {
    "Secret": "[REDACTED — production-grade JWT signing key, minimum 32 characters]"
  },
  "AppConfig": {
    "FrontendBaseUrl": "https://portalgerencial-test.alpla.net"
  }
}
```

> **IMPORTANT:** The workflow preserves this file during deployment. If it is accidentally deleted, it must be recreated manually with the correct secrets.

### 10.3 Database Requirements

- The `[Portal-Gerencial-Test]` database must exist on the SQL Server instance.
- EF Core migrations must be applied manually before the API can process requests.
- The SQL login used in the connection string must have `db_owner` permissions on the database.
- Migrations are run manually via `sqlcmd` or SSMS — **never automated** by the workflow.

---

## 11. Troubleshooting

### Frontend shows blank page

**Symptom:** `index.html` loads but JS/CSS return 404.

**Cause:** The `assets/` subdirectory was not preserved during deployment.

**Check:**
```powershell
# Verify the assets directory exists:
Test-Path "C:\Apps\AlplaPortal\Test\web\assets"
Get-ChildItem "C:\Apps\AlplaPortal\Test\web\assets" -Filter "*.js"
Get-ChildItem "C\Apps\AlplaPortal\Test\web\assets" -Filter "*.css"
```

**Fix (v2.155.1):** The workflow now uses `robocopy /E` instead of `Copy-Item` to preserve the Vite `dist/` directory structure, and validates that `assets/` exists before uploading artifacts.

### Login returns 404 with /api/api/ path

**Symptom:** Browser shows `POST /api/api/auth/login 404`.

**Cause:** The frontend `API_BASE_URL` defaulted to `/api`, but every endpoint path already included `/api/...`, producing a double prefix.

**Fix (v2.155.1):** `API_BASE_URL` default changed from `'/api'` to `''`. In development, `VITE_API_BASE_URL=http://localhost:5000` provides the full base. In production/TEST, the empty default produces same-origin calls like `/api/auth/login`.

### API returns 500 Internal Server Error

**Diagnosis checklist:**

| # | Check | Command / Location |
|:---:|:---|:---|
| 1 | `ASPNETCORE_ENVIRONMENT` is set to `Test` | IIS Manager → App Pool → Advanced Settings → Environment Variables |
| 2 | `appsettings.Test.json` exists | `Test-Path "C:\Apps\AlplaPortal\Test\api\appsettings.Test.json"` |
| 3 | Connection string is valid | Check `appsettings.Test.json` → `ConnectionStrings.DefaultConnection` |
| 4 | Database exists | `sqlcmd -S . -d "Portal-Gerencial-Test" -Q "SELECT 1"` |
| 5 | Migrations are applied | Check Event Viewer or API startup logs for migration errors |
| 6 | JWT secret is configured | Check `appsettings.Test.json` → `Jwt.Secret` (min 32 chars) |
| 7 | SQL permissions are correct | Verify the SQL login has `db_owner` on `[Portal-Gerencial-Test]` |
| 8 | Event Viewer errors | Event Viewer → Windows Logs → Application → Source: IIS AspNetCore Module |
| 9 | IIS stdout logs | Check `C:\Apps\AlplaPortal\Test\api\logs\stdout_*.log` (enable in web.config if needed) |

**Common causes:**
- Empty or missing connection string → startup migration fails silently, subsequent DB requests return 500.
- Missing `appsettings.Test.json` → app falls back to `appsettings.json` with empty connection string.
- Wrong `ASPNETCORE_ENVIRONMENT` → app runs as Production, doesn't load `appsettings.Test.json`.

### Smoke test fails
- The API may need additional time to start after IIS App Pool restart.
- Check IIS App Pool state: `Get-WebAppPoolState -Name "AlplaPortal-Test-Api-Pool"`
- Check API logs for startup errors.
- Verify the health URL is correct and accessible from localhost.

### Robocopy exit codes
- Exit codes 0–7 are success/informational (files copied, skipped, etc.).
- Exit code 8+ indicates an error (access denied, network error, etc.).

### IIS App Pool won't start
- Check Event Viewer → Windows Logs → Application for ASP.NET Core errors.
- Verify the .NET 8 Hosting Bundle is installed on the server.
- Ensure `web.config` in the API directory is valid.

### Reverse proxy returns 500 or 502
- Verify ARR is installed: IIS Manager → Server → Application Request Routing Cache.
- Verify ARR proxy is enabled: Server Proxy Settings → "Enable proxy" must be checked.
- Verify the API site is running: `Get-WebAppPoolState -Name "AlplaPortal-Test-Api-Pool"`
- Verify API is listening on port 5001: `Get-NetTCPConnection -LocalPort 5001`
- Check the frontend `web.config` rule: `type="Rewrite" url="http://localhost:5001/api/{R:1}"`

---

## 12. Security Notes

- **No secrets are stored in the workflow file or repository.**
- Connection strings, JWT secrets, and integration credentials are configured in `appsettings.Test.json` on the server and/or as IIS App Pool environment variables.
- The workflow uses only GitHub environment **variables** (not secrets) for paths and non-sensitive configuration.
- The workflow preserves `appsettings.*.json` files on the server during deployment — they are never overwritten by the build artifact.
- All `[REDACTED]` values in documentation are placeholders — real values are never committed.

---

## 13. Post-Deployment Issue Log

### v2.155.0 → v2.155.1 (First Deployment Fixes)

| Issue | Root Cause | Fix |
|:---|:---|:---|
| **Blank page** — JS/CSS 404 | `Copy-Item` flattened Vite `dist/assets/` during artifact staging | Replaced with `robocopy /E` + validation step |
| **Login 404** — `/api/api/auth/login` | `API_BASE_URL` defaulted to `/api` but endpoints already had `/api/` prefix | Default changed to `''` (empty string) |
| **API 500** — Internal Server Error | Missing `appsettings.Test.json`, no `ASPNETCORE_ENVIRONMENT`, empty connection string | Server config checklist documented; requires manual admin setup |
| **No reverse proxy** — browser can't reach API | Frontend and API on separate IIS sites, no routing from `/api/*` to `localhost:5001` | Added `web.config` with URL Rewrite + ARR reverse proxy rule |

