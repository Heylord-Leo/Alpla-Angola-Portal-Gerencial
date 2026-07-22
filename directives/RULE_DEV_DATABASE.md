# RULE — Canonical Local Development Database

## 1. Objective

This directive defines the mandatory database environment for all local Development work, including application startup, EF Core migrations, agent-driven testing, bug reproduction, and data correction.

## 2. Canonical Database

| Property | Value |
|:---|:---|
| **Database** | `Portal-Gerencial-Dev-ProdClone` |
| **SQL Instance** | `(localdb)\MSSQLLocalDB` |
| **Nature** | Refreshable local snapshot of Production data |
| **Refresh Pipeline** | `scripts/db/import-prod-data-dev.ps1` |
| **Canonical Startup** | `execution/restart_services.ps1` |
| **EF Migrations** | `execution/update_dev_database.ps1` |

## 3. Forbidden Databases

| Database | Reason |
|:---|:---|
| `AlplaPortalV1` | Obsolete. Decommissioned. Must never be recreated. |
| `Portal-Gerencial` | Production. Never connect from local tooling. |
| `Portal-Gerencial-Test` | TEST environment. Managed by deployment pipeline only. |

## 4. Isolation Requirements

- **Integration tests** must use `Portal-Gerencial-IntegrationTests` or a per-run unique database. They must never use the Development clone, TEST, or PROD.
- **Demo/seed tools** must use a separate sandbox database and must never target the clone.
- **Schema changes** must use EF Core migrations (`dotnet ef migrations add`).
- **Data corrections** must be repeatable SQL scripts, never ad-hoc manual edits.

## 5. Safety Model

Before any data-changing SQL operation, verify:

1. The SQL instance is exactly `(localdb)\MSSQLLocalDB`.
2. `DB_NAME()` returns exactly `Portal-Gerencial-Dev-ProdClone`.
3. The operation is not targeting TEST or PROD.

Never assume a database target from a filename or configuration value. Always verify the actual instance and actual `DB_NAME()` before writes.

## 6. Startup Safety

The following multi-layer safety model prevents accidental connection to wrong databases:

1. **`appsettings.Development.json`** — points to `Portal-Gerencial-Dev-ProdClone` (gitignored, local-only).
2. **`execution/restart_services.ps1`** — validates `DB_NAME()` before launching backend.
3. **`Program.cs` Development guard** — queries `DB_NAME()` at runtime and throws if not the canonical clone.

All three layers must agree. The canonical startup script (`execution/restart_services.ps1`) is the only supported entry point.
