# Portal Attendance Foundation — Validation Report (v2.97.0)

> **Date**: 2026-05-12  
> **Version**: v2.97.0 (Phases 1 & 2)  
> **Author**: AI-Assisted Validation  
> **Status**: ⚠️ PARTIALLY VALIDATED — Issues found, adjustments recommended before Phase 3

---

## 1. Infrastructure Verification

| Check | Result | Notes |
|---|---|---|
| `dotnet build` | ✅ Pass | 0 errors, 25 warnings (pre-existing NuGet advisories) |
| Backend startup | ✅ Pass | Listening on http://localhost:5000 |
| Frontend startup | ✅ Pass | Listening on http://localhost:5173 |
| Swagger accessible | ✅ Pass | Both portal endpoints visible in HRAttendance section |
| Calendar UI unchanged | ✅ Pass | R.H. > Presenças loads and displays attendance data as before |
| DI registration | ✅ Pass | Both services resolve correctly at runtime |

---

## 2. Schedule Resolver Tests

### Test 2.1 — Normal Day Shift (ADMINISTRATIVO Plan)

| Field | Value |
|---|---|
| **Employee** | 1517 (RECURSOS HUMANOS) |
| **Date** | 2026-05-05 (Tuesday) |
| **Plan** | 10 — ADMINISTRATIVO (Padrão, 7-day cycle) |
| **Resolved Day Index** | 1 |
| **Schedule** | "08 as 17:30" (08:00–17:30) |
| **Expected Minutes** | 570 (9h30m) |
| **Rest Day** | ❌ (correct) |
| **Overnight** | ❌ (correct) |
| **Periods** | 1 mandatory: 08:00–17:30, Tol.Entry=15min, Tol.Exit=45min |
| **Result** | ✅ **PASS** |

### Test 2.2 — Rest Day (Weekend)

| Field | Value |
|---|---|
| **Employee** | 1517 (RECURSOS HUMANOS) |
| **Date** | 2026-05-10 (Saturday) |
| **Schedule** | "Descanso" (Dia de descanso), Sigla="DC" |
| **Rest Day** | ✅ true |
| **Expected Minutes** | 0 |
| **Periods** | None |
| **Result** | ✅ **PASS** |

### Test 2.3 — Escala (Shift Rotation) Plan Employee

| Field | Value |
|---|---|
| **Employee** | 1714 (PRODUÇÃO) |
| **Date** | 2026-05-05 |
| **Plan** | 5 — Escala-08h-20h (Escala type, CycleDays=0) |
| **Result** | ⚠️ **404 (Not Found)** |
| **Root Cause** | Escala plans do NOT use `PlanosTrabalhoHorarios` for day-to-schedule mapping. Schedule assignment for shift rotation employees occurs at the `Alteracoes` level directly. |

### Test 2.4 — Night Shift Plan Employee

| Field | Value |
|---|---|
| **Employee** | N/A (Plan 11 — Escala-20h-08h) |
| **Result** | ⚠️ **Not testable** |
| **Root Cause** | Same as Test 2.3 — night shift plans are all Escala type |

> **CRITICAL FINDING — F-SCH-01**: The `PortalScheduleResolver` only works for `Padrão` (Standard) type work plans. Out of 161 scoped employees, only **58** (36%) are on standard plans (Plan 10: 44 employees, Plan 6: 14 employees). The remaining **103** employees (64%) use Escala-type plans with `CycleDays=0`, which have no `PlanosTrabalhoHorarios` data.

---

## 3. Punch Interpreter Tests

### Test 3.1 — Standard EN/SA Directions (High Confidence)

| Field | Value |
|---|---|
| **Employee** | 1570 (INFORMÁTICA) |
| **Date** | 2026-04-21 |
| **Punches** | 2: EN@08:06, SA@17:35 |
| **Interpretation** | Entry@08:06 (StandardEN), Exit@17:35 (StandardSA) |
| **Worked** | 569 min (9h29m) — matches expected 570 |
| **Confidence** | ✅ **High** |
| **Warnings** | None |
| **Result** | ✅ **PASS** |

### Test 3.2 — Empty Direction / Position-Based Inference (Medium Confidence)

| Field | Value |
|---|---|
| **Employee** | 1517 (RECURSOS HUMANOS) |
| **Date** | 2026-05-05 |
| **Punches** | 2: null@07:30, null@17:32 |
| **Interpretation** | Entry@07:30 (InferredFirstEntry), Exit@17:32 (InferredLastExit) |
| **Worked** | 602 min (10h02m) — slightly over expected 570 |
| **Confidence** | ✅ **Medium** (correct for inferred) |
| **Pair** | Complete (07:30 → 17:32) |
| **Result** | ✅ **PASS** |

### Test 3.3 — Code 17/18 Interpretation

| Field | Value |
|---|---|
| **Employee** | 1714 (PRODUÇÃO) |
| **Date** | 2026-04-21 |
| **Punches** | 3: 17@07:49, 18@20:31, 17@08:02 |
| **Interpretation** | Entry@07:49 (Code17Entry), Exit@20:31 (Code18Exit), Entry@08:02 (Code17Entry) |
| **Worked** | 762 min (12h42m) — from pair 07:49→20:31 |
| **Confidence** | Low |
| **Pairs** | Complete (07:49→20:31 = 762min), MissingExit (08:02→?) |
| **Result** | ⚠️ **PARTIAL** — Correct for main pair, but 08:02 is a duplicate entry not flagged |

### Test 3.4 — Code 17 Used For Both Directions

| Field | Value |
|---|---|
| **Employee** | 1517 (RECURSOS HUMANOS) |
| **Date** | 2026-05-08 |
| **Punches** | 2: 17@07:34, 17@14:13 |
| **Interpretation** | Entry@07:34 (Code17Entry), Entry@14:13 (Code17Entry) |
| **Worked** | 0 min |
| **Confidence** | Low |
| **Warnings** | 2× "Entry punch has no matching Exit — missing exit" |
| **Result** | ⚠️ **FAIL** — Code 17 is incorrectly assumed to always be Entry |

> **CRITICAL FINDING — F-PCH-01**: Code 17 is NOT a reliable entry direction indicator. In practice, terminals are sending Code 17 for BOTH entry and exit punches. The interpreter's `Code17Entry` / `Code18Exit` rule produces incorrect results in these cases. The correct approach is to treat Code 17/18 the same as empty direction and use position-based inference.

### Test 3.5 — All-Code-17 Punches

| Field | Value |
|---|---|
| **Employee** | 1534 (PRODUÇÃO) |
| **Date** | 2026-04-21 |
| **Punches** | 3: 17@07:56, 17@20:20, 17@07:52 |
| **Interpretation** | All 3 interpreted as Entry (Code17Entry) |
| **Worked** | 0 min (no pairs formed) |
| **Confidence** | Low |
| **Expected** | 07:52→20:20 ≈ 748 min (12h28m) |
| **Result** | ❌ **FAIL** — Code 17 rule prevents valid pair formation |

### Test 3.6 — Single Punch (Missing Exit)

| Field | Value |
|---|---|
| **Employee** | 1659 (PRODUÇÃO) |
| **Date** | 2026-04-21 |
| **Punches** | 1: 17@07:56 |
| **Interpretation** | Entry@07:56 (Code17Entry) |
| **Worked** | 0 min |
| **Confidence** | Low |
| **Warnings** | "1 punch(es) with unknown direction could not be paired" |
| **Result** | ✅ **PASS** — Correctly identifies incomplete attendance |

### Test 3.7 — Zero Punches (Absent)

| Field | Value |
|---|---|
| **Employee** | 1758 (INFORMÁTICA) |
| **Date** | 2026-05-05 |
| **Punches** | 0 |
| **Confidence** | None |
| **Result** | ✅ **PASS** — Correctly reports no attendance data |

### Test 3.8 — Duplicate Entry Punch (Not Flagged)

| Field | Value |
|---|---|
| **Employee** | 1714 (PRODUÇÃO) |
| **Date** | 2026-04-21 |
| **Punch 1** | 17@07:49 → Entry |
| **Punch 3** | 17@08:02 → Entry (13 minutes after first Entry) |
| **Duplicate Flagged** | ❌ `isDuplicateCandidate = false` |
| **Result** | ⚠️ **FAIL** — Duplicate entry within 15-minute window not detected |

> **FINDING — F-PCH-02**: The duplicate detection logic does not detect consecutive punches of the same interpreted direction within a short time window. The 08:02 Entry punch, occurring 13 minutes after the 07:49 Entry, should be flagged as `isDuplicateCandidate = true`.

---

## 4. Cross-Comparison: Portal vs Innux Calendar

| Employee | Date | Innux Calendar | Portal Interpretation | Match? | Notes |
|---|---|---|---|---|---|
| 1517 | 2026-05-05 | Data present (calendar loads) | Entry 07:30 → Exit 17:32, 602min, Medium conf | ✅ Consistent | Portal adds transparency on direction inference |
| 1517 | 2026-05-07 | Data present | Entry 07:40 → Exit 17:33, 593min, Medium conf | ✅ Consistent | |
| 1517 | 2026-05-08 | Data present | 2 punches, 0min (Code 17 issue) | ❌ Portal wrong | Code 17 rule produces 0 worked minutes |
| 1570 | 2026-04-21 | Data present | Entry 08:06 → Exit 17:35, 569min, High conf | ✅ Consistent | Standard EN/SA — perfect result |
| 1714 | 2026-04-21 | Likely ~12h worked | 762min (07:49→20:31) + MissingExit | ⚠️ Partially wrong | Main pair is correct but duplicate punch not flagged |

---

## 5. Summary of Findings

### ✅ Working Correctly

1. **Standard plan schedule resolution** — Padrão plans with 7-day cycles resolve accurately
2. **Rest day detection** — Saturday/Sunday correctly identified as "Descanso"
3. **Standard EN/SA punch interpretation** — High confidence, accurate results
4. **Empty direction inference** — Position-based Entry/Exit inference works well
5. **Single/zero punch handling** — Correct low-confidence reporting
6. **Warning generation** — Clear, actionable warnings
7. **Confidence scoring** — Appropriate levels (High/Medium/Low/None)
8. **UI preservation** — Calendar UI completely unaffected
9. **Swagger registration** — Both endpoints visible and documented

### ⚠️ Issues Requiring Adjustment Before Phase 3

| ID | Severity | Description | Impact |
|---|---|---|---|
| **F-SCH-01** | 🔴 High | Schedule resolver fails for all Escala-type plans (64% of employees) | No schedule context for majority of workforce |
| **F-PCH-01** | 🔴 High | Code 17/18 treated as fixed Entry/Exit, but terminals use them for both directions | Produces 0 worked minutes when both punches are Code 17 |
| **F-PCH-02** | 🟡 Medium | Duplicate punch detection not working (same-direction punches within time window not flagged) | Creates false "MissingExit" pairs |

---

## 6. Recommended Adjustments Before Phase 3

### 6.1 — Code 17/18 Rule Change (F-PCH-01) — **PRIORITY 1**

**Current**: `Code17 → Entry`, `Code18 → Exit`  
**Proposed**: Treat Code 17/18 as equivalent to empty direction and apply position-based inference  
**Rationale**: Terminal data shows Code 17 used for both directions. Position-based inference (first=Entry, last=Exit) produces more accurate results.

### 6.2 — Escala Plan Fallback (F-SCH-01) — **PRIORITY 2**

**Current**: Schedule resolution only works via `PlanosTrabalhoHorarios` day mapping  
**Proposed**: For Escala plans, attempt to resolve the schedule from `Alteracoes.IDHorario` as a fallback. This reads the schedule that Innux already assigned for the day.  
**Rationale**: This preserves the diagnostic purpose (we still get schedule context) without requiring the Portal to implement its own rotation logic.

### 6.3 — Duplicate Detection Enhancement (F-PCH-02) — **PRIORITY 3**

**Current**: No duplicate detection  
**Proposed**: Flag consecutive punches with the same interpreted direction within a configurable time window (e.g., 15 minutes) as `isDuplicateCandidate = true`  
**Rationale**: Prevents false "MissingExit" pairs and improves data quality.

---

## 7. Test Employees Reference

| Innux ID | Department | Work Plan | Plan Type | Direction Codes | Useful For |
|---|---|---|---|---|---|
| 1517 | RECURSOS HUMANOS | 10 (ADMINISTRATIVO) | Padrão | null, Code 17 | Schedule + Empty dir + Code 17 |
| 1570 | INFORMÁTICA | 10 (ADMINISTRATIVO) | Padrão | EN/SA | Standard direction testing |
| 1714 | PRODUÇÃO | 5 (Escala-08h-20h) | Escala | Code 17/18 | Code 17/18 + Duplicates |
| 1534 | PRODUÇÃO | 5 (Escala-08h-20h) | Escala | Code 17 only | All-same-code testing |
| 1586 | PRODUÇÃO | Unknown | Escala | Code 17/18 | Mixed code + duplicate |
| 1758 | INFORMÁTICA | 10 (ADMINISTRATIVO) | Padrão | N/A | Zero-punch (absent) |

---

## 8. Conclusion

**Phases 1 & 2 are architecturally sound but require two critical adjustments before Phase 3 can begin.** The foundation (DTOs, interfaces, service infrastructure, DI, endpoint registration, authorization) is solid. The issues are confined to interpretation rules:

1. The Code 17/18 direction assumption is incorrect and should be changed to position-based inference.
2. The schedule resolver needs a fallback path for Escala-type plans.

These adjustments are **backward-compatible** (no schema changes, no UI changes, no production data changes) and can be implemented within the existing Phase 2 boundary.
