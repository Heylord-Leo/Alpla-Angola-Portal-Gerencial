# EF migration chain — not replayable from an empty database (duplicate `SourceCompany` add)

Status: **BACKLOG — pre-existing defect, recorded 2026-10-29, deliberately NOT fixed in v2.249.4.** No application code,
migration or environment was changed for this item; it was found while rehearsing the v2.249.4 migration on a disposable
LocalDB database (`PoDocumentSupersessionMigrationRehearsalTests`).

## Defect

Two consecutive migrations add the same two columns to `ItemCatalogItems`:

| Migration | Operations (Up) |
|---|---|
| `20260603151258_AddItemCatalogSourceCompany` | `AddColumn SourceCompany nvarchar(max) NULL`, `AddColumn LastSyncedAtUtc datetime2 NULL` |
| `20260603152331_AddItemCatalogSourceCompanyFix` | the **same** two `AddColumn` operations again |

Replaying the chain with `Database.Migrate()` (or `IMigrator.MigrateAsync`) on an **empty** database therefore stops at
`20260603152331_AddItemCatalogSourceCompanyFix` with SQL Server error 2705:
`Column names in each table must be unique. Column name 'SourceCompany' in table 'ItemCatalogItems' is specified more than once.`

## Why the environments are not affected

DEV clone, TEST and PROD were never built from zero by `Migrate()`. They receive the **idempotent** SQL that `dotnet ef`
generates for the pending range (`.github/workflows/apply-migrations-test.yml`, `apply-migrations-prod.yml`), where each
migration is wrapped in `IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = …)`. In those databases one of
the two June migrations was already recorded when the other ran, so the duplicate never executed. The model snapshot and the
current schema agree (`dotnet ef migrations has-pending-model-changes`: none).

## Where it matters

- Any attempt to create a database from the migration chain alone (a fresh environment, a disposable rehearsal database, a
  future "restore schema from migrations" step) fails at the June migration.
- Test sandboxes in this repository avoid the chain on purpose: they are model-created (`EnsureCreated`) and rebuilt on
  drift; the v2.249.4 rehearsal builds the pre-migration schema as "current model minus the new objects + history rows" and then
  applies the real idempotent script for the single new migration.

## Options (decision pending, separate task)

1. Make `20260603152331_AddItemCatalogSourceCompanyFix` idempotent in place (guard both `AddColumn` with
   `IF COL_LENGTH(...) IS NULL` via `migrationBuilder.Sql`). Lowest risk for existing databases (already recorded in
   `__EFMigrationsHistory`, so the body never runs there again).
2. Leave both migrations untouched and document that bootstrapping a new database must start from a schema baseline.

Rule reminder: never run `Down`; never edit a migration that an environment still has to apply (both June migrations are
applied everywhere today).
