# Post-Deployment Checklist — PRODUCTION

**Environment:** PRODUCTION  
**Server:** AOVIA1VMS011  
**URL:** https://portalgerencial.alpla.net/  
**API Port:** 5002  
**Database:** `[Portal-Gerencial]`

---

## Automated Checks (Workflow)

These are performed automatically by the `deploy-prod.yml` workflow:

- [ ] Database name validation: target is `[Portal-Gerencial]` (not Test)
- [ ] SQL database backup created
- [ ] File backups created
- [ ] API files deployed (config files preserved)
- [ ] Web files deployed (web.config preserved, port 5002)
- [ ] App Pools restarted
- [ ] Smoke test passed (API health endpoint returns 200)
- [ ] Test environment App Pools still running

---

## Manual Validation (by Leonardo)

### Application Access

| # | Check | How | Status |
|:---:|:---|:---|:---:|
| 1 | **Web loads** | Open `https://portalgerencial.alpla.net/` in browser | [ ] |
| 2 | **HTTPS active** | Verify padlock icon (no mixed content warnings) | [ ] |
| 3 | **HTTP redirects** | Open `http://portalgerencial.alpla.net/` — should redirect to HTTPS | [ ] |
| 4 | **API responds** | From server: `Invoke-WebRequest http://localhost:5002/health` | [ ] |

### Login & Core Features

| # | Check | How | Status |
|:---:|:---|:---|:---:|
| 5 | **Login works** | Log in with `leonardo.cintra@alpla.com` | [ ] |
| 6 | **Dashboard loads** | Verify KPI cards render after login | [ ] |
| 7 | **Navigation works** | Click through main menu items | [ ] |
| 8 | **API calls work** | Check browser DevTools Network tab — no 404/500 errors | [ ] |

### Integration Providers

| # | Check | How | Status |
|:---:|:---|:---|:---:|
| 9 | **Primavera** | Settings → Integrations → Test Primavera connection | [ ] |
| 10 | **AlplaPROD** | Settings → Integrations → Test AlplaPROD per-plant connections | [ ] |
| 11 | **Innux** | Settings → Integrations → Test Innux connection | [ ] |
| 12 | **Not pointing to Test** | Verify integration providers are NOT using Test database/server | [ ] |

### Server-Side Validation

| # | Check | Command | Status |
|:---:|:---|:---|:---:|
| 13 | **API process running** | `Get-WebAppPoolState -Name "AlplaPortal-Prod-Api-Pool"` | [ ] |
| 14 | **Web pool running** | `Get-WebAppPoolState -Name "AlplaPortal-Prod-Web-Pool"` | [ ] |
| 15 | **Port 5002 listening** | `Get-NetTCPConnection -LocalPort 5002 -State Listen` | [ ] |
| 16 | **Port 5000 free** | `Get-NetTCPConnection -LocalPort 5000` (should be used by other app only) | [ ] |
| 17 | **Logs written** | `Get-ChildItem "C:\Apps\AlplaPortal\Prod\logs"` | [ ] |
| 18 | **Uploads path writable** | Create/delete a test file in `C:\Apps\AlplaPortal\Prod\uploads` | [ ] |

### Test Environment Not Impacted

| # | Check | Command | Status |
|:---:|:---|:---|:---:|
| 19 | **Test Web loads** | Open `https://portalgerencial-test.alpla.net/` | [ ] |
| 20 | **Test API responds** | From server: `Invoke-WebRequest http://localhost:5001/health` | [ ] |
| 21 | **Test pools running** | `Get-WebAppPoolState -Name "AlplaPortal-Test-Api-Pool"` | [ ] |
| 22 | **Test DB untouched** | Verify `[Portal-Gerencial-Test]` was not modified | [ ] |

### Validation Script

Run the automated validation script on the server:

```powershell
cd C:\path\to\scripts\server
.\validate-production-environment.ps1
```

All checks should show `[PASS]`.

---

## First-Time Deployment Additional Steps

If this is the first ever Production deployment:

| # | Step | Status |
|:---:|:---|:---:|
| A | Database `[Portal-Gerencial]` exists with correct schema | [ ] |
| B | `appsettings.Production.json` configured with connection string, JWT secret | [ ] |
| C | Admin user `leonardo.cintra@alpla.com` seeded (via UI or SQL template) | [ ] |
| D | SSL certificate installed and bound to HTTPS 443 | [ ] |
| E | DNS resolves `portalgerencial.alpla.net` to AOVIA1VMS011 | [ ] |
| F | ARR proxy enabled in IIS Manager (server-level setting) | [ ] |
