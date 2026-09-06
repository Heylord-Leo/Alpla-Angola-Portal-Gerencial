# Dashboard V2 (v2.240.0) — Deployment & Backfill Runbook

> Status: DEV complete (migration applied, backfill applied). TEST/PROD **not** started. No deployment is
> performed by this document — it records the required order for the operator.

## Why order matters

The Stage Aging ("Gargalos") section reads `OperationalStageState` snapshots. Those rows are created by
(a) **live capture** for entities that transition after the migration is deployed, and (b) a one-shot
**backfill** for entities that already exist. Until the backfill runs in a target environment, the section
shows an honest empty state ("Não há etapas com medição de permanência ativa no seu escopo"), even though
operational work exists. Therefore the schema + backfill must precede the frontend cutover in each
environment.

## Deployment sequence (per environment: TEST, then PROD)

1. **Deploy the schema migration** `20260905203852_AddOperationalStageTracking`
   (additive: two tables + five indexes; no data or destructive operation). If the deployment pipeline
   applies EF migrations automatically before the app starts, this happens as part of the normal deploy —
   confirm it ran.
2. **Verify schema:** `OperationalStageStates` and `OperationalStageTransitions` exist with their five
   indexes (incl. the unique `(EntityType, EntityId)` index). Both tables start empty.
3. **Run the Stage Aging backfill DRY-RUN** (privileged maintenance action — see below). Writes nothing.
4. **Review dry-run counts / evidence:** in-scope total, reliable vs unknown, and the by-stage breakdown.
   Reliable timestamps are expected only for `AREA_APPROVAL` (batch creation) and `FIN_SCHEDULED` (the
   group's latest scheduled-payment creation); all other stages are honestly unknown. Expect **no Buyer**
   and **no FIN_PAID** stages.
5. **Run the backfill APPLY** (privileged maintenance action, explicit confirmation).
6. **Verify post-apply:** no Buyer/REQUEST snapshots; no FIN_PAID snapshot; **zero** BACKFILL-produced
   transition-history rows; one snapshot per entity; known/unknown counts consistent; LIVE precedence intact
   (a live transition during/after backfill always wins — BACKFILL never overwrites LIVE).
7. **Deploy / activate the frontend** with the canonical Stage Aging section.

If the environment's pipeline naturally applies migrations before the frontend goes live, steps 1–2 fold
into the normal deploy; steps 3–6 remain an explicit operator action between migration and frontend
activation.

## Backfill operator notes (privileged maintenance)

- Endpoint: `api/v1/admin/stage-aging/backfill`, restricted to **System Administrator**.
- `GET /preview` — dry-run: classifies every in-scope entity and returns proposed counts. Writes nothing.
- `POST /apply?confirm=true` — applies the idempotent backfill (refuses without `confirm=true`).
- **Safety guarantees:** BACKFILL **never** overwrites a LIVE snapshot (database-enforced conditional writes);
  it creates **current snapshots only** and **never** reconstructs historical `OperationalStageTransition`
  events; it is idempotent (safe to rerun — a second run proposes zero writes).
- The DEV reference run produced 253 in-scope snapshots (118 with a reliable historical timestamp, 135
  honestly unknown, 0 fabricated transitions). Exact counts differ per environment with legitimate workflow
  evolution.

## Known limitations (not defects)

- Historical `StageEnteredAtUtc` may be unavailable for entities that already existed before tracking began;
  the DEV reference was ~53% unknown after backfill. Unknown age is shown honestly, never as 0 and never as
  overdue.
- Buyer/REQUEST aging is out of scope in this release (Buyer stays visible via the Pipeline and Alerts
  sections).
- Finance and Documentation stages have no severity threshold — age is shown, classification is neutral.
- Stage Aging rows are managerial read-only in this release (no drill-down navigation).
