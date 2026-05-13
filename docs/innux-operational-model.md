# Innux Attendance — Operational Data Model for Portal Integration

> **Baseline**: [innux-schema-mapping.md](file:///c:/dev/alpla-portal/docs/innux-schema-mapping.md)  
> **Date**: 2026-04-22  
> **Status**: Validated — Ready for integration design

---

## 1. Source of Truth per Business Need

| Business Need | Source Table | Role | Notes |
|---|---|---|---|
| Raw entry/exit punches | `TerminaisMarcacoes` | Transactional | `TipoProcessado`: `EN`=Entry, `SA`=Exit |
| Daily processed attendance | `Alteracoes` | Transactional (computed) | **Primary source for calendar views** |
| Attendance period breakdown | `AlteracoesPeriodos` | Transactional (computed) | Detail rows per `Alteracoes` record |
| Accumulated hours by code | `AlteracoesAcumulados` | Transactional (computed) | Payroll-oriented aggregation |
| Absence records | `Ausencias` | Transactional | Planned + approved + rejected absences |
| Absence audit trail | `AusenciasLog` | Log (append-only) | Read for audit, not for display |
| Work schedule definition | `Horarios` + `HorariosPeriodos` | Reference | Schedule rules + time windows |
| Work plan / shift rotation | `PlanosTrabalho` + `PlanosTrabalhoHorarios` | Reference | Maps schedules to cycle days |
| Employee assigned schedule | `Funcionarios.IDPlanoTrabalho` | Master | Points to the employee's active plan |
| Absence code legend | `CodigosAusencia` | Lookup | 66 codes: unjustified, justified, vacation, illness… |
| Work code legend | `CodigosTrabalho` | Lookup | 204 codes: basic, overtime 150%, 200%… |
| Real-time presence | `Presencas` | Volatile (live) | Refreshed by Innux engine continuously |

---

## 2. Table Classification

### Transactional (high-volume, date-filtered)
| Table | Rows | Growth | Filter Strategy |
|---|---:|---|---|
| `Alteracoes` | 112,549 | ~700/day | `WHERE Data BETWEEN @start AND @end` |
| `AlteracoesPeriodos` | 75,964 | child of above | `JOIN Alteracoes` + date filter |
| `AlteracoesAcumulados` | 64,487 | child of above | `JOIN Alteracoes` + date filter |
| `TerminaisMarcacoes` | 74,962 | ~400/day | `WHERE Data BETWEEN @start AND @end` |
| `Ausencias` | 8,433 | variable | `WHERE Data BETWEEN @start AND @end` |

### Reference / Lookup (small, cacheable)
| Table | Rows | Cache Strategy |
|---|---:|---|
| `CodigosAusencia` | 66 | Cache on startup, refresh daily |
| `CodigosTrabalho` | 204 | Cache on startup, refresh daily |
| `Horarios` | 26 | Cache on startup |
| `HorariosPeriodos` | 33 | Cache on startup |
| `PlanosTrabalho` | 11 | Cache on startup |
| `PlanosTrabalhoHorarios` | 2,506 | Cache on startup |
| `Departamentos` | 51 | Already synced to Portal |
| `Feriados` | 18 | Cache on startup |
| `Terminais` | 3 | Cache on startup |
| `Entidades` | 2 | Cache on startup |

### Master Data
| Table | Rows | Notes |
|---|---:|---|
| `Funcionarios` | 161 | Already synced via `HREmployeeSyncService` |

### Volatile / Real-Time
| Table | Rows | Notes |
|---|---:|---|
| `Presencas` | 112 | Overwritten by Innux engine; query on demand |

---

## 3. Relationship Paths

```
Funcionarios (IDFuncionario)
├── → TerminaisMarcacoes.IDFuncionario     [1:N] Raw punches
├── → Alteracoes.IDFuncionario             [1:N] Daily attendance
│   ├── → AlteracoesPeriodos.IDAlteracao   [1:N] Period breakdown
│   │   ├── → CodigosTrabalho.IDCodigoTrabalho  [N:1] Work code
│   │   └── → CodigosAusencia.IDCodigoAusencia  [N:1] Absence code
│   ├── → AlteracoesAcumulados.IDAlteracao [1:N] Accumulated hours
│   │   ├── → CodigosTrabalho.IDCodigoTrabalho  [N:1]
│   │   └── → CodigosAusencia.IDCodigoAusencia  [N:1]
│   ├── → Horarios.IDHorario               [N:1] Schedule used that day
│   └── → PlanosTrabalho.IDPlanoTrabalho   [N:1] Work plan used
├── → Ausencias.IDFuncionario              [1:N] Absence records
│   ├── → CodigosAusencia.IDCodigoAusencia [N:1]
│   └── → AusenciasLog.IDAusencia          [1:N] Audit trail
├── → Presencas.IDFuncionario              [1:1] Live presence
├── → Departamentos.IDDepartamento         [N:1] Department
├── → PlanosTrabalho.IDPlanoTrabalho       [N:1] Assigned work plan
│   └── → PlanosTrabalhoHorarios           [1:N] Day-to-schedule map
│       └── → Horarios.IDHorario           [N:1] Schedule definition
│           └── → HorariosPeriodos         [1:N] Time windows
└── → Entidades.IDEntidade                 [N:1] Company entity
```

> [!WARNING]
> **All joins are implicit** — no declared FK constraints exist. Always use `LEFT JOIN` for optional relationships and validate join results defensively.

---

## 4. Key Columns Shortlist for Portal Consumption

| Business Field | Source Table | Column | Type | Notes |
|---|---|---|---|---|
| Employee ID | `Funcionarios` | `IDFuncionario` | int | Join key everywhere |
| Employee Number | `Funcionarios` | `Numero` | nvarchar | Display identifier |
| Employee Name | `Funcionarios` | `Nome` | nvarchar | Full name |
| Department | `Departamentos` | `Descricao` | nvarchar | Via `f.IDDepartamento` |
| Card Number | `Funcionarios` | `Cartao` | nvarchar | Badge ID |
| Attendance Date | `Alteracoes` | `Data` | datetime | Date part only |
| First Entry | `Alteracoes` | `Entrada1` | datetime | Time part = actual clock-in |
| First Exit | `Alteracoes` | `Saida1` | datetime | Time part = actual clock-out |
| Absence Duration | `Alteracoes` | `Falta` | datetime | `1900-01-01T08:00` = 8h absent |
| Justified Absence | `Alteracoes` | `Ausencia` | datetime | Same convention |
| Expected Hours | `Alteracoes` | `Objectivo` | datetime | `1900-01-01T08:00` = 8h expected |
| Balance | `Alteracoes` | `Saldo` | datetime | Worked − Expected |
| Anomaly | `Alteracoes` | `TipoAnomalia` | nvarchar | Free text |
| Justification | `Alteracoes` | `Justificacao` | nvarchar | Free text |
| Validated | `Alteracoes` | `Validado` | bit | HR-validated flag |
| Raw Punch Direction | `TerminaisMarcacoes` | `TipoProcessado` | nvarchar(2) | `EN` / `SA` |
| Raw Punch Time | `TerminaisMarcacoes` | `Hora` | datetime | Time part only |
| Absence Code | `CodigosAusencia` | `Codigo` | nvarchar | e.g., `FLTINJ`, `FER` |
| Absence Description | `CodigosAusencia` | `Descricao` | nvarchar | e.g., "Falta Injustificada" |
| Absence Type | `CodigosAusencia` | `TipoCodigo` | int | 0=Unjustified, 1=Vacation, 2=Justified |
| Work Code | `CodigosTrabalho` | `Codigo` | nvarchar | e.g., `Basico`, `Extra` |
| Work Description | `CodigosTrabalho` | `Descricao` | nvarchar | e.g., "Trabalho Extra 150%" |
| Schedule Code | `Horarios` | `Codigo` | nvarchar | e.g., `DIASEM` |
| Schedule Description | `Horarios` | `Descricao` | nvarchar | e.g., "Dia de Semana" |
| Presence Status | `Presencas` | `Estado` | nvarchar | `Presente` / `Ausente` |
| Is Late | `Presencas` | `Atrasado` | bit | Boolean flag |
| Lateness | `Presencas` | `Atraso` | datetime | Duration |

### Time Conversion Formula
```sql
-- Convert Innux datetime-as-duration to minutes:
DATEDIFF(MINUTE, '1900-01-01', column_value)

-- Convert to decimal hours:
DATEDIFF(MINUTE, '1900-01-01', column_value) / 60.0

-- Convert to HH:MM display string:
CONVERT(VARCHAR(5), column_value, 108)
```

---

## 5. Use Case Recommendations

### A. Team Attendance Calendar

**Goal**: Show a monthly/weekly grid of attendance status per employee per day.

**Primary source**: `Alteracoes` — one row per employee per day, already computed.

**Query strategy**:
```sql
SELECT a.Data, f.Numero, f.Nome, d.Descricao AS Departamento,
       h.Codigo AS HorarioCodigo, h.Descricao AS Horario,
       CONVERT(VARCHAR(5), a.Entrada1, 108) AS PrimeiraEntrada,
       CONVERT(VARCHAR(5), a.Saida1, 108) AS PrimeiraSaida,
       DATEDIFF(MINUTE, '1900-01-01', a.Falta) AS FaltaMinutos,
       DATEDIFF(MINUTE, '1900-01-01', a.Ausencia) AS AusenciaMinutos,
       DATEDIFF(MINUTE, '1900-01-01', a.Objectivo) AS ObjectivoMinutos,
       DATEDIFF(MINUTE, '1900-01-01', a.Saldo) AS SaldoMinutos,
       a.Validado, a.TipoAnomalia, a.Justificacao, a.Marcacao AS NumMarcacoes,
       a.FaltouPeriodosObrigatorios
FROM Alteracoes a
INNER JOIN Funcionarios f ON a.IDFuncionario = f.IDFuncionario
INNER JOIN Departamentos d ON f.IDDepartamento = d.IDDepartamento
LEFT JOIN Horarios h ON a.IDHorario = h.IDHorario
WHERE a.Data BETWEEN @StartDate AND @EndDate
  AND f.Activo = 1
ORDER BY a.Data, d.Descricao, f.Nome
```

**Calendar cell logic**:
| Condition | Cell Display |
|---|---|
| `Entrada1 IS NOT NULL AND Falta = 0` | ✅ Present (green) |
| `Falta > 0 AND Ausencia > 0` | 🟡 Justified absence (yellow) |
| `Falta > 0 AND Ausencia = 0` | 🔴 Unjustified absence (red) |
| `Objectivo = 0` (rest day schedule) | ⚪ Day off (grey) |
| `TipoAnomalia IS NOT NULL AND <> ''` | ⚠️ Anomaly (orange) |

---

### B. Employee Daily Attendance Detail

**Goal**: Drill-down showing all punches, periods, and codes for one employee on one day.

**Strategy**: Combine `Alteracoes` (summary) + `TerminaisMarcacoes` (raw punches) + `AlteracoesPeriodos` (period breakdown).

**Query 1 — Summary**:
```sql
SELECT a.*, h.Codigo AS HorarioCodigo, h.Descricao AS HorarioDesc,
       pt.Codigo AS PlanoCodigo, pt.Descricao AS PlanoDesc
FROM Alteracoes a
LEFT JOIN Horarios h ON a.IDHorario = h.IDHorario
LEFT JOIN PlanosTrabalho pt ON a.IDPlanoTrabalho = pt.IDPlanoTrabalho
WHERE a.IDFuncionario = @IDFuncionario AND a.Data = @Data
```

**Query 2 — Raw punches**:
```sql
SELECT tm.Hora, tm.TipoProcessado, t.Nome AS Terminal, tm.Gerada,
       tm.Latitude, tm.Longitude
FROM TerminaisMarcacoes tm
LEFT JOIN Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.IDFuncionario = @IDFuncionario AND tm.Data = @Data
ORDER BY tm.Hora
```

**Query 3 — Period breakdown**:
```sql
SELECT ap.Inicio, ap.Fim,
       ct.Codigo AS CodigoTrabalho, ct.Descricao AS DescTrabalho,
       ca.Codigo AS CodigoAusencia, ca.Descricao AS DescAusencia,
       ap.Dispensa, ap.CentroCusto
FROM AlteracoesPeriodos ap
INNER JOIN Alteracoes a ON ap.IDAlteracao = a.IDAlteracao
LEFT JOIN CodigosTrabalho ct ON ap.IDCodigoTrabalho = ct.IDCodigoTrabalho
LEFT JOIN CodigosAusencia ca ON ap.IDCodigoAusencia = ca.IDCodigoAusencia
WHERE a.IDFuncionario = @IDFuncionario AND a.Data = @Data
ORDER BY ap.Inicio
```

---

### C. Absence / Leave Visualization

**Goal**: Show absence calendar and leave requests with status.

**Primary source**: `Ausencias` (request-level) enriched with `CodigosAusencia`.

```sql
SELECT aus.Data, aus.Tipo AS TipoPeriodo,
       CASE aus.Tipo WHEN 0 THEN 'Dia Inteiro'
                     WHEN 1 THEN 'Manhã'
                     WHEN 2 THEN 'Tarde' END AS PeriodoDesc,
       ca.Codigo, ca.Descricao AS TipoAusencia, ca.Cor,
       ca.TipoCodigo,
       CASE ca.TipoCodigo WHEN 0 THEN 'Injustificada'
                          WHEN 1 THEN 'Férias'
                          WHEN 2 THEN 'Justificada' END AS Classificacao,
       aus.Pendente, aus.Planificado, aus.DataPedido,
       aus.Observacoes, aus.Documento,
       f.Numero, f.Nome, d.Descricao AS Departamento
FROM Ausencias aus
INNER JOIN Funcionarios f ON aus.IDFuncionario = f.IDFuncionario
INNER JOIN Departamentos d ON f.IDDepartamento = d.IDDepartamento
INNER JOIN CodigosAusencia ca ON aus.IDCodigoAusencia = ca.IDCodigoAusencia
WHERE aus.Data BETWEEN @StartDate AND @EndDate
  AND f.Activo = 1
ORDER BY aus.Data, d.Descricao, f.Nome
```

---

### D. Current Presence — Who Is In / Out

**Primary source**: `Presencas` (live, volatile).

```sql
SELECT p.Estado, p.Atrasado,
       DATEDIFF(MINUTE, '1900-01-01', p.Atraso) AS AtrasoMinutos,
       CONVERT(VARCHAR(5), p.UltimaMarcacao, 108) AS UltimaMarcacao,
       p.Tipo AS UltimaDirecao,
       f.Numero, f.Nome, d.Descricao AS Departamento,
       h.Codigo AS HorarioCodigo, h.Descricao AS Horario,
       t.Nome AS Terminal
FROM Presencas p
INNER JOIN Funcionarios f ON p.IDFuncionario = f.IDFuncionario
LEFT JOIN Departamentos d ON p.IDDepartamento = d.IDDepartamento
LEFT JOIN Horarios h ON p.IDHorario = h.IDHorario
LEFT JOIN Terminais t ON p.IDTerminal = t.IDTerminal
WHERE f.Activo = 1
ORDER BY p.Estado DESC, d.Descricao, f.Nome
```

---

### E. Shift / Schedule Explanation per Employee

**Goal**: Show which schedule applies to an employee on any given date.

**Strategy**: `Funcionarios.IDPlanoTrabalho` → `PlanosTrabalho` → `PlanosTrabalhoHorarios` → `Horarios` → `HorariosPeriodos`.

```sql
-- Step 1: Employee's work plan
SELECT f.Numero, f.Nome, pt.Codigo AS PlanoCodigo, pt.Descricao AS PlanoDesc,
       pt.Tipo AS PlanoTipo, pt.NumeroDias AS CicloDias, pt.DataInicio AS CicloInicio
FROM Funcionarios f
INNER JOIN PlanosTrabalho pt ON f.IDPlanoTrabalho = pt.IDPlanoTrabalho
WHERE f.IDFuncionario = @IDFuncionario

-- Step 2: Schedule for each day of the cycle
SELECT pth.Dia, h.Codigo AS HorarioCodigo, h.Descricao AS Horario,
       CONVERT(VARCHAR(5), h.InicioHorario, 108) AS Inicio,
       CONVERT(VARCHAR(5), h.FimHorario, 108) AS Fim,
       DATEDIFF(MINUTE, '1900-01-01', h.HorasNormais) / 60.0 AS HorasNormais
FROM PlanosTrabalhoHorarios pth
INNER JOIN Horarios h ON pth.IDHorario = h.IDHorario
WHERE pth.IDPlanoTrabalho = @IDPlanoTrabalho
ORDER BY pth.Dia

-- Step 3: Time windows for a specific schedule
SELECT hp.Tipo, CONVERT(VARCHAR(5), hp.Inicio, 108) AS Inicio,
       CONVERT(VARCHAR(5), hp.Fim, 108) AS Fim,
       CONVERT(VARCHAR(5), hp.ToleranciaEntrada, 108) AS TolEntrada,
       CONVERT(VARCHAR(5), hp.ToleranciaSaida, 108) AS TolSaida,
       ct.Descricao AS CodigoTrabalho
FROM HorariosPeriodos hp
LEFT JOIN CodigosTrabalho ct ON hp.IDCodigoTrabalho = ct.IDCodigoTrabalho
WHERE hp.IDHorario = @IDHorario
ORDER BY hp.Inicio
```

**Calculating schedule for a specific date**:
```sql
-- For weekly plans (Tipo='Semanal', NumeroDias=7):
-- Dia 0 = Monday (or plan-defined start), Dia 6 = Sunday
-- DayOffset = DATEDIFF(DAY, pt.DataInicio, @TargetDate) % pt.NumeroDias
```

---

## 6. Validation Diagnostic Queries

### Q1 — Raw Punches (last 7 days)
```sql
SELECT TOP 50 tm.Data, CONVERT(VARCHAR(5), tm.Hora, 108) AS Hora,
       tm.TipoProcessado, f.Numero, f.Nome, t.Nome AS Terminal
FROM TerminaisMarcacoes tm
INNER JOIN Funcionarios f ON tm.IDFuncionario = f.IDFuncionario
LEFT JOIN Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.Data >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
ORDER BY tm.Data DESC, tm.Hora DESC
```

### Q2 — Processed Daily Attendance (last 7 days)
```sql
SELECT TOP 50 a.Data, f.Numero, f.Nome,
       CONVERT(VARCHAR(5), a.Entrada1, 108) AS Entrada1,
       CONVERT(VARCHAR(5), a.Saida1, 108) AS Saida1,
       DATEDIFF(MINUTE, '1900-01-01', a.Falta) AS FaltaMin,
       DATEDIFF(MINUTE, '1900-01-01', a.Objectivo) AS ObjMin,
       DATEDIFF(MINUTE, '1900-01-01', a.Saldo) AS SaldoMin,
       a.Validado, a.TipoAnomalia, a.Marcacao AS Punches
FROM Alteracoes a
INNER JOIN Funcionarios f ON a.IDFuncionario = f.IDFuncionario
WHERE a.Data >= DATEADD(DAY, -7, CAST(GETDATE() AS DATE))
ORDER BY a.Data DESC, f.Nome
```

### Q3 — Absences with Code Descriptions (last 30 days)
```sql
SELECT aus.Data, f.Numero, f.Nome,
       ca.Codigo, ca.Descricao AS TipoAusencia,
       CASE ca.TipoCodigo WHEN 0 THEN 'Injustificada'
                          WHEN 1 THEN 'Ferias'
                          WHEN 2 THEN 'Justificada' END AS Classificacao,
       aus.Pendente, aus.Observacoes
FROM Ausencias aus
INNER JOIN Funcionarios f ON aus.IDFuncionario = f.IDFuncionario
INNER JOIN CodigosAusencia ca ON aus.IDCodigoAusencia = ca.IDCodigoAusencia
WHERE aus.Data >= DATEADD(DAY, -30, CAST(GETDATE() AS DATE))
ORDER BY aus.Data DESC, f.Nome
```

### Q4 — Employee Schedule/Plan Mapping
```sql
SELECT f.Numero, f.Nome, f.Activo,
       pt.Codigo AS PlanoCodigo, pt.Descricao AS PlanoDesc,
       pt.Tipo AS PlanoTipo, pt.NumeroDias,
       d.Descricao AS Departamento
FROM Funcionarios f
LEFT JOIN PlanosTrabalho pt ON f.IDPlanoTrabalho = pt.IDPlanoTrabalho
LEFT JOIN Departamentos d ON f.IDDepartamento = d.IDDepartamento
WHERE f.Activo = 1
ORDER BY d.Descricao, f.Nome
```

### Q5 — Current Presence
```sql
SELECT p.Estado, p.Atrasado,
       DATEDIFF(MINUTE, '1900-01-01', p.Atraso) AS AtrasoMin,
       CONVERT(VARCHAR(5), p.UltimaMarcacao, 108) AS UltimaMarcacao,
       p.Tipo, f.Numero, f.Nome, d.Descricao AS Dept
FROM Presencas p
INNER JOIN Funcionarios f ON p.IDFuncionario = f.IDFuncionario
LEFT JOIN Departamentos d ON p.IDDepartamento = d.IDDepartamento
WHERE f.Activo = 1
ORDER BY p.Estado DESC, d.Descricao, f.Nome
```

### Q6 — Punch vs. Processed Attendance Cross-Validation
```sql
-- Compare raw punch count vs Alteracoes.Marcacao field for a specific day
SELECT f.Numero, f.Nome,
       a.Marcacao AS ProcessedPunchCount,
       (SELECT COUNT(*) FROM TerminaisMarcacoes tm
        WHERE tm.IDFuncionario = f.IDFuncionario
          AND tm.Data = a.Data) AS RawPunchCount,
       CONVERT(VARCHAR(5), a.Entrada1, 108) AS ProcessedEntry1,
       (SELECT TOP 1 CONVERT(VARCHAR(5), tm.Hora, 108)
        FROM TerminaisMarcacoes tm
        WHERE tm.IDFuncionario = f.IDFuncionario
          AND tm.Data = a.Data AND tm.TipoProcessado = 'EN'
        ORDER BY tm.Hora) AS RawFirstEntry,
       a.Validado
FROM Alteracoes a
INNER JOIN Funcionarios f ON a.IDFuncionario = f.IDFuncionario
WHERE a.Data = @TargetDate AND f.Activo = 1
ORDER BY f.Nome
```

---

## 7. Risks, Ambiguities & Assumptions

### Risks

| Risk | Impact | Mitigation |
|---|---|---|
| **No FK constraints** | Orphan records possible; joins may produce unexpected NULLs | Always use `LEFT JOIN` + `COALESCE` for display fields |
| **Connection latency** | Innux server needs 21+ seconds for initial handshake from dev | Use connection pooling; increase timeout for initial connect; cache aggressively |
| **Time storage as datetime** | `1900-01-01T00:00:00` is "zero hours" but is a non-null value | Use `DATEDIFF(MINUTE, '1900-01-01', col)` — never compare to NULL for zero-check |
| **Negative balance** | `Saldo` can encode negative values but datetime can't go below `1900-01-01` | Investigate how Innux stores negative saldo — may use a separate sign convention |
| **Volatile Presencas** | Real-time table overwritten continuously by Innux | Never cache; always query on demand; handle stale reads gracefully |
| **Month closing** | `FechosMes` locks processed months — data for closed months may not update | Filter awareness: warn users if querying a closed month |

### Assumptions

1. `Alteracoes` is the **authoritative** processed result — it supersedes raw punches for any HR decision
2. `TerminaisMarcacoes` is the **audit trail** — used for drill-down and validation, not as primary display
3. `Ausencias` operates independently of `Alteracoes` — an absence in `Ausencias` may or may not be reflected in `Alteracoes.Ausencia` depending on whether the day has been processed
4. The Innux engine runs periodic processing that converts raw punches into `Alteracoes` records — there is a time lag between a punch and its reflection in the processed table
5. `Funcionarios.IDFuncionario` in Innux maps to the employee code already synced via `HREmployeeSyncService`

> [!NOTE]
> **Portal Implementation — PunchCount Source (2026-04-25)**
>
> The Portal uses `Alteracoes.Marcacao` as the official processed punch count for **both** the calendar grid and the detail drawer. This is the Innux engine's canonical value.
>
> `TerminaisMarcacoes` (raw terminal punches) is queried **only** in the detail drill-down as supporting/debug evidence. The raw count is exposed separately as `RawPunchCount` and is never used to determine the attendance status classification.
>
> These two values may legitimately differ when: punches were purged after processing, days were manually validated by HR in Innux, or marks were imported from external systems.
>
> If `Alteracoes.Marcacao > 0` but `TerminaisMarcacoes` has zero rows, the Portal shows an informational banner in the drawer explaining the discrepancy.

### Open Questions

> [!IMPORTANT]
> **Q1**: How does Innux encode **negative balance** in `Alteracoes.Saldo`? Datetime cannot be negative. Needs sample validation with a known-negative employee day.

> [!IMPORTANT]
> **Q2**: What is the exact processing schedule for the Innux engine? (How often does it convert `TerminaisMarcacoes` → `Alteracoes`?) This determines the freshness guarantee.

> [!IMPORTANT]
> **Q3**: The `Presencas` table has only 112 rows but 161 active employees. Are some employees excluded from presence tracking? Needs validation.

---

## 8. Final Recommendation — Portal Integration Architecture

### Recommended Approach: Backend Read-Only Service Layer

```
Portal Frontend
    ↓ (REST API)
Portal Backend (AlplaPortal.Api)
    ↓ (InnuxConnectionFactory — SQL Auth)
Innux Database (read-only queries)
```

### Integration Service Design

| Service | Innux Tables | Caching | Refresh |
|---|---|---|---|
| `InnuxAttendanceService` | `Alteracoes` + joins | Short TTL (5 min) | On-demand + periodic |
| `InnuxPunchService` | `TerminaisMarcacoes` | No cache | On-demand only |
| `InnuxAbsenceService` | `Ausencias` + `CodigosAusencia` | Short TTL (5 min) | On-demand |
| `InnuxPresenceService` | `Presencas` | No cache | On-demand only |
| `InnuxScheduleService` | `Horarios`, `PlanosTrabalho`, etc. | Long TTL (1 hour) | Startup + periodic |
| `InnuxLookupService` | `CodigosAusencia`, `CodigosTrabalho` | Long TTL (1 hour) | Startup + periodic |

### Key Design Principles

1. **Read-only** — never write to Innux; it is an external system of record
2. **Backend-proxied** — all queries go through the Portal backend; frontend never touches Innux directly
3. **Scoped by Portal ACL** — apply the Portal's existing access scoping (System Admin / Local Manager / Self) to filter `IDFuncionario` results, just like the Team Calendar already does
4. **Cache reference data** — lookup tables change rarely; cache them to reduce cross-server round trips
5. **Date-range queries only** — always filter transactional tables by `Data BETWEEN @start AND @end`; never do full-table scans
6. **Defensive joins** — use `LEFT JOIN` everywhere due to absent FK constraints
7. **Time conversion at the service layer** — convert Innux `datetime-as-duration` to proper `TimeSpan` / minutes at the C# level, not in SQL

---

## 10. Portal-Side Attendance Interpretation Engine (v2.97.0)

> **Date**: 2026-05-12
> **Status**: Foundation Deployed (Phases 1 & 2)
> **Decision**: DEC-120

### Purpose

The Portal-Side Attendance Interpretation Engine is a **read-only, diagnostic "shadow" engine** that interprets raw attendance data independently of Innux's processed `Alteracoes` output. It was created because:

1. Innux `Alteracoes` exhibits persistent issues: duplicated entries, false absence classifications (F03), contradictory vacation/shift data, and opaque interpretation of terminal direction codes.
2. The Portal cannot write to or modify Innux or Primavera databases.
3. A transparent, auditable interpretation layer was needed to diagnose discrepancies and build confidence before any production transition.

### Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                   HRAttendanceController                      │
│  ┌─────────────────────────┐  ┌────────────────────────────┐ │
│  │ /portal/resolve-schedule │  │ /portal/interpret-punches  │ │
│  └────────────┬────────────┘  └─────────────┬──────────────┘ │
│               │                              │                │
│  ┌────────────▼────────────┐  ┌─────────────▼──────────────┐ │
│  │  IPortalScheduleResolver│  │  IPortalPunchInterpreter   │ │
│  │  (Phase 1)              │  │  (Phase 2)                 │ │
│  └────────────┬────────────┘  └─────────────┬──────────────┘ │
│               │                              │                │
│  ┌────────────▼──────────────────────────────▼──────────────┐ │
│  │              Innux Database (READ-ONLY)                   │ │
│  │  PlanosTrabalho, PlanosTrabalhoHorarios, HorariosPeriodos │ │
│  │  TerminaisMarcacoes, Funcionarios                         │ │
│  └───────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### Phase 1 — Schedule Day Resolver (`PortalScheduleResolver`)

**Input**: `innuxEmployeeId` + `date`
**Output**: `ResolvedScheduleDayDto`

**Logic**:
1. Query `PlanosTrabalho` to get the employee's assigned work plan (`CicloInicio`, `NumeroDias`).
2. Compute cycle day offset: `(targetDate - CicloInicio).TotalDays % NumeroDias + 1`.
3. Map to `PlanosTrabalhoHorarios` to find the `IDHorario` for that cycle day.
4. Hydrate from `HorariosPeriodos` to get entry/exit times and expected working minutes.
5. Detect overnight shifts where entry time > exit time (e.g., 22:00–06:00).
6. Identify rest days (no periods or all periods with 0 expected minutes).

**Tables Accessed**: `PlanosTrabalho`, `PlanosTrabalhoHorarios`, `HorariosPeriodos`, `Funcionarios`

### Phase 2 — Raw Punch Interpreter (`PortalPunchInterpreter`)

**Input**: `innuxEmployeeId` + `date` (+ resolved schedule from Phase 1)
**Output**: `PunchInterpretationResultDto`

**Logic**:
1. Query `TerminaisMarcacoes` for all punches on the target date (plus next-day punches for overnight shifts).
2. **Direction Inference** (3 strategies in priority order):
   - **Standard**: `TipoProcessado` = `EN` → Entry, `SA` → Exit.
   - **Alternate Codes**: `TipoProcessado` = `17` → Entry, `18` → Exit.
   - **Position-Based**: Empty/unknown `TipoProcessado` → alternating Entry/Exit starting from Entry.
3. **Duplicate Detection**: Punches within 2-minute window with same direction are flagged as `IsDuplicateCandidate` but **never removed** (transparency principle).
4. **Pair Building**: Match Entry→Exit pairs in chronological order.
5. **Worked Minutes**: Calculate per pair and total.
6. **Confidence Score**:
   - `High`: Standard EN/SA codes.
   - `Medium`: Alternate 17/18 codes.
   - `Low`: Position-based inference.
   - `None`: Unable to interpret.

**Tables Accessed**: `TerminaisMarcacoes`

### Diagnostic Endpoints

| Endpoint | Purpose | Auth |
|---|---|---|
| `GET /api/hr/attendance/portal/resolve-schedule/{innuxEmployeeId}/{date}` | Resolve expected schedule | SystemAdmin, HR |
| `GET /api/hr/attendance/portal/interpret-punches/{innuxEmployeeId}/{date}` | Interpret raw punches | SystemAdmin, HR |

**Important**: These endpoints are **diagnostic only**. They are NOT consumed by the production HR Attendance Calendar UI.

### Safety Constraints

- ✅ All SQL queries are `SELECT`-only with parameterized inputs.
- ✅ Zero writes to Innux.
- ✅ Zero writes to Primavera.
- ✅ No changes to the existing `InnuxAttendanceService` or calendar behavior.
- ✅ No changes to existing API responses consumed by the HR Calendar.

### Phase 3 — Comparison Engine (v2.98.0, hardened v2.98.1)

Phase 3 implements `AttendanceComparisonService`, a diagnostic comparison engine that contrasts Innux processed attendance against Portal raw-punch interpretation. Implemented in v2.98.0 (2026-05-12), hardened in v2.98.1.

#### Architecture

The service is a pure orchestrator — no new SQL queries. It calls:
1. `IInnuxAttendanceService.GetDailyAttendanceAsync()` — Innux processed result from `Alteracoes`.
2. `IInnuxAttendanceService.GetWorkedHoursAsync()` — Innux worked-minutes enrichment from `AlteracoesPeriodos` (v2.98.1).
3. `IPortalPunchInterpreter.InterpretPunchesAsync()` — Portal raw-punch interpretation from `TerminaisMarcacoes`.
4. `IPortalScheduleResolver.ResolveScheduleForDateAsync()` — Schedule context from `PlanosTrabalho`/`HorariosPeriodos` (or `Alteracoes.IDHorario` fallback for Escala plans).

**Important:** The `Alteracoes.IDHorario` fallback provides schedule context ONLY. It is NOT treated as proof of attendance. Portal attendance evidence comes exclusively from raw punches interpreted by `PortalPunchInterpreter`.

#### Innux Worked-Minutes Enrichment (v2.98.1)

`GetDailyAttendanceAsync` (calendar grid query) does not merge `AlteracoesPeriodos`, so `TotalWorkedMinutes` is always 0. When `InnuxStatus ∈ {Present, PortalInterpreted, Anomaly}` and `InnuxWorkedMinutes == 0`, the engine enriches from `GetWorkedHoursAsync`:

| Enrichment Result | `InnuxWorkedMinutesSource` | `InnuxWorkedMinutesEnriched` | Comparison Behavior |
|---|---|---|---|
| `AlteracoesPeriodos` has data | `DayDetail` | `true` | Uses enriched value for drift comparison |
| `AlteracoesPeriodos` empty | `NotAvailable` | `false` | Severity = Low (informational), no false Medium |
| Enrichment error | `NotAvailable` | `false` | Same as empty — graceful degradation |
| No enrichment needed (status not present) | `CalendarSummary` | `false` | Original value used (0 is expected) |

**Note:** In the current Innux deployment for Alpla Angola, `AlteracoesPeriodos` is consistently empty across all employees and dates tested. This means enrichment always yields `NotAvailable`. If Innux configuration changes in the future and populates `AlteracoesPeriodos`, the enrichment will automatically start providing `DayDetail` values.

#### Portal Status Derivation

| Condition | PortalStatus |
|---|---|
| No raw punches found, schedule is rest day | `DayOff` |
| No raw punches found, schedule is work day | `NoPunches` |
| Schedule is rest day, punches exist | `PresentOnRestDay` |
| Complete punch pairs found, worked > 0 | `Present` |
| Punches exist but no complete pair | `Incomplete` |

#### Discrepancy Rules (Explicit Mappings)

| Severity | Condition | Example Message |
|---|---|---|
| **HIGH** | Innux Absent/DayOff/Vacation/Holiday/JustifiedAbsence + Portal Present | "Revisar: o Innux indica Ausência, mas o Portal encontrou picagens válidas com entrada e saída." |
| **HIGH** | Innux Present + Portal NoPunches | "Revisar: o Innux indica presença, mas o Portal não encontrou picagens brutas no terminal." |
| **MEDIUM** | Both Present, worked diff > 30min | "Revisar: diferença superior a 30 minutos entre Innux (Xmin) e Portal (Ymin)." |
| **MEDIUM** | Both Present, entry/exit drift > 30min | "Revisar: hora de entrada difere mais de 30 minutos." |
| **MEDIUM** | Innux Present + Portal Incomplete | "Revisar manualmente: o Innux indica presença, mas o Portal encontrou picagem incompleta." |
| **MEDIUM** | Duplicates detected | "Apenas informativo: N picagem(ns) duplicada(s) detectada(s) pelo Portal." |
| **LOW** | Worked diff 1-30min | "Apenas informativo: diferença de X minutos." |
| **LOW** | Schedule resolved via Alteracoes fallback | "Apenas informativo: turno resolvido via fallback Alteracoes.IDHorario." |
| **LOW** | Portal confidence = Low | "Apenas informativo: confiança do Portal é baixa para esta interpretação." |
| **NONE** | All dimensions aligned | "Sem divergência relevante." |

#### Diagnostic Endpoints

Restricted to `SystemAdministrator` and `HR` roles.

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/hr/attendance/portal/compare/{innuxEmployeeId}/{date}` | Single employee-day comparison |
| `GET` | `/api/hr/attendance/portal/compare-range?startDate=&endDate=&innuxEmployeeId=&departmentId=&onlyDiscrepancies=true` | Range comparison (max 31 days) |

#### Range Safeguards

- Maximum date range: **31 days**. Exceeding returns HTTP 400.
- Execution time and employee-day count are logged at `Information` level.
- Batch processing is sequential (per employee, per day) — no parallel SQL. If performance becomes a concern in future multi-department scans, optimization can be considered separately.

#### Safety Constraints (Unchanged)

- ✅ All SQL queries are `SELECT`-only with parameterized inputs.
- ✅ Zero writes to Innux.
- ✅ Zero writes to Primavera.
- ✅ No changes to the existing `InnuxAttendanceService` or calendar behavior.
- ✅ No changes to existing API responses consumed by the HR Calendar.
- ✅ Comparison engine is diagnostic only — does not replace the production calendar source.

---

### Phase 4 — Diagnostic Review UI (v2.99.0)

Frontend-only diagnostic page that visualizes the comparison engine output for HR/Admin users.

#### Route & Access

| Property | Value |
|---|---|
| Route | `/hr/attendance-review` |
| Tab Label | Revisão de Presenças |
| Tab Icon | `ShieldCheck` (lucide-react) |
| Tab Visibility | System Administrator, HR only |
| Page-Level Guard | `AdminRoute` + component-level role check |
| Department Manager Access | **Blocked** (both tab and direct URL) |

#### UI Components

1. **Diagnostic Banner** — Persistent informational bar: "Esta tela é apenas diagnóstica. Nenhuma informação é gravada no Innux ou Primavera."
2. **Filter Bar** — Start date, End date, Innux Employee ID, Severity dropdown, "Apenas divergências" toggle, Search button. Client-side 31-day validation.
3. **Summary KPI Cards** — Total days, No discrepancy (gray), Low (blue), Medium (orange), High (red), Execution time.
4. **Results Table** — 13 columns: Data, Funcionário, Status Innux, Status Portal, Severidade, Ent. Innux, Ent. Portal, Saída Innux, Saída Portal, Min. Innux, Min. Portal, Confiança, Ação.
5. **Detail Drawer** — Slide-in right panel showing:
   - Innux processed data (status, entry, exit, worked/expected minutes, source, enrichment)
   - Portal interpreted data (status, entry, exit, worked/expected minutes, schedule source)
   - Discrepancy messages with severity-colored styling
   - Portal warnings
   - Recommended review action
   - Raw punches timeline (on-demand from `interpret-punches`)
   - Punch pairs with worked minutes

#### Severity Visual Mapping

| Severity | Color | Meaning |
|---|---|---|
| High | Red (#dc2626) | Requires HR review — status contradiction |
| Medium | Orange (#ea580c) | Relevant discrepancy — time/minutes drift |
| Low | Blue (#2563eb) | Informational — minor issue or limited data |
| None | Gray (#6b7280) | Aligned — no discrepancy |

#### API Consumption

Consumes existing Phase 3 diagnostic endpoints (no new backend endpoints):

- `GET /api/hr/attendance/portal/compare-range` — Filter bar search
- `GET /api/hr/attendance/portal/interpret-punches/{id}/{date}` — Drawer punch detail (on-demand)

#### Safety Constraints (Unchanged)

- ✅ Strictly read-only — no write actions anywhere on the page.
- ✅ No approve, reject, correct, or synchronize buttons.
- ✅ Does not change the official HR Attendance Calendar calculation.
- ✅ Does not make Portal interpretation the default attendance source.
- ✅ Zero writes to Innux or Primavera.
