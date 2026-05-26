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

## 9. Troubleshooting

### Smoke test fails
- The API may need additional time to start after IIS App Pool restart.
- Check IIS App Pool state: `Get-WebAppPoolState -Name "AlplaPortal-Test-Api-Pool"`
- Check API logs for startup errors.
- Verify the health URL is correct and accessible from localhost.

### Robocopy exit codes
- Exit codes 0–7 are success/informational (files copied, skipped, etc.).
- Exit code 8+ indicates an error (access denied, network error, etc.).

### Configuration file issues
- If `appsettings.Test.json` is missing on the server, it must be created manually with the correct connection strings and settings.
- The workflow preserves but does not create these files.

### IIS App Pool won't start
- Check Event Viewer → Windows Logs → Application for ASP.NET Core errors.
- Verify the .NET 8 Hosting Bundle is installed on the server.
- Ensure `web.config` in the API directory is valid.

---

## 10. Security Notes

- **No secrets are stored in the workflow file or repository.**
- Connection strings, JWT secrets, and integration credentials are configured as IIS App Pool environment variables directly on the server.
- The workflow uses only GitHub environment **variables** (not secrets) for paths and non-sensitive configuration.
- All `[REDACTED]` values in documentation are placeholders — real values are never committed.
