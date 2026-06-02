# Operations Live Transfer Board — Design Document

> **Status**: v2.179.0 — TV Signage UX Redesign
> **Date**: 2026-06-01
> **Author**: AI Agent
> **Parent Document**: [OPERATIONS_MODULE_TECHNICAL_DESIGN.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md)

> [!IMPORTANT]
> Backend endpoint: Phase Live 2 (v2.177.0). Frontend TV page: Phase Live 3 (v2.178.0). **TV signage UX redesign: v2.179.0** — compact cards, SVG timeline icons, auto-paging carousel (no scrollbars), large KPI bar, short attention messages. Route: `/operations/live-board/:plant`.

---

## 1. Goal

Create an Operations Live Transfer Board — a visual, TV-ready screen for logistics/dock areas that shows inter-plant material transfers as an easy-to-understand timeline.

The board answers four questions immediately:

1. **What is coming in?** — Inbound transfers arriving at this plant
2. **What is going out?** — Outbound transfers leaving this plant
3. **What stage is each transfer in?** — Visual timeline per transfer
4. **What needs attention?** — Delayed or partially received transfers highlighted

This is **not** an administrative table. It is a visual operations board for people in the plant.

---

## 2. Target Audience

| Audience | Context |
|----------|---------|
| Logistics team | Plan dock operations, prioritize unloading/loading |
| Warehouse / dock operators | Know what trucks to expect, what to prepare |
| Production support | Track raw material arrivals for production scheduling |
| Supervisors | Overview of transfer flow, identify bottlenecks |
| Plant managers | Quick status check from a distance |

### 2.1 Usage Context

The screen will be displayed on:
- **TV / digital signage** in logistics areas (outbound docks, inbound receiving, warehouse offices)
- **Kiosk terminals** near loading/unloading bays
- **Desktop browsers** for supervisors who want a quick dashboard

**Example scenario**: At the Viana 1 outbound loading dock, the logistics team needs to know:
- What is arriving from Viana 2 to Viana 1 (inbound)
- What is leaving Viana 1 to Viana 3 (outbound)
- Which transfers are waiting for receipt
- Which are partially received
- Which are completed
- Which need attention (delayed, stuck)

### 2.2 Viewing Distance

Content must be readable from **3–5 meters** on a standard 40–55" TV screen. This drives minimum font sizes, card sizes, and color contrast requirements.

---

## 3. Core UX Principles

### 3.1 Design Rules

| Rule | Rationale |
|------|-----------|
| **Understandable without training** | Dock workers should read the board on day one |
| **Large visual cards** | Readable from across the room |
| **Clear directional flow** | Inbound (left) vs outbound (right) instantly clear |
| **Simple timeline stages** | 4–5 stages max, no technical event codes |
| **Strong status colors** | Green/amber/red visible at a glance |
| **Minimal text** | Material name + quantity + stage — no noise |
| **Auto-refresh** | No manual interaction required for TV mode |
| **TV/kiosk-first layout** | Full-screen, no sidebar, no scroll |

### 3.2 Avoid

| Avoid | Why |
|-------|-----|
| Dense tables | Not readable from a distance |
| SQL/table names (`T_Wareneingaenge`) | Meaningless to operators |
| Technical IDs as primary content | Use PO number + material name instead |
| Too many columns | Overwhelms non-IT users |
| Small text (< 16px on screen) | Unreadable on TV |
| Values only IT understands | `IdJournalStatus = 62` means nothing |
| Financial values | Security — visible on public TV |
| Usernames | Security — visible on public TV |

---

## 4. Visual Layout

### 4.1 Screen Structure

```text
┌─────────────────────────────────────────────────────────────────┐
│  ■ LIVE TRANSFER BOARD — VIANA 1                                │
│  Última atualização: 10:42:15 │ Próxima atualização em 48s     │
├────────────────────────────────┬────────────────────────────────┤
│  ⬅ ENTRADAS PARA VIANA 1      │  SAÍDAS DE VIANA 1 ➡          │
│  (Inbound)                     │  (Outbound)                    │
├────────────────────────────────┼────────────────────────────────┤
│                                │                                │
│  ┌──────────────────────────┐  │  ┌──────────────────────────┐  │
│  │ Transfer Card            │  │  │ Transfer Card            │  │
│  └──────────────────────────┘  │  └──────────────────────────┘  │
│                                │                                │
│  ┌──────────────────────────┐  │  ┌──────────────────────────┐  │
│  │ Transfer Card            │  │  │ Transfer Card            │  │
│  └──────────────────────────┘  │  └──────────────────────────┘  │
│                                │                                │
│  ┌──────────────────────────┐  │  ┌──────────────────────────┐  │
│  │ Transfer Card            │  │  │ Transfer Card            │  │
│  └──────────────────────────┘  │  └──────────────────────────┘  │
│                                │                                │
│  +2 transferências em fila     │  Sem transferências ativas     │
│                                │                                │
├────────────────────────────────┴────────────────────────────────┤
│  ■ Resumo: 5 entradas │ 3 saídas │ 2 atenção │ 1 concluído    │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 Header Bar

| Element | Description |
|---------|-------------|
| Plant name | `VIANA 1` — large, bold, left-aligned |
| Last update time | `Última atualização: HH:MM:SS` |
| Countdown | `Próxima atualização em XXs` — animated countdown |
| Connection status | Green dot (connected) / amber dot (stale) / red dot (error) |

### 4.3 Two-Column Layout

| Column | Content |
|--------|---------|
| **Left: Entradas (Inbound)** | Transfers arriving at this plant from other plants |
| **Right: Saídas (Outbound)** | Transfers leaving this plant to other plants |

Each column header shows:
- Direction arrow (⬅ inbound, ➡ outbound)
- Column title in Portuguese
- Count of active transfers

### 4.4 Summary Footer

A single-line summary bar at the bottom:
```
■ Resumo: 5 entradas │ 3 saídas │ 2 atenção │ 1 concluído recente
```

---

## 5. Transfer Card Design

### 5.1 Card Layout

```text
┌─────────────────────────────────────────────────┐
│  #3429                      🟠 Parcialmente     │
│  Viana 2 → Viana 1              recebido        │
│                                                 │
│  Purchased CM Cap 28mm CSDB Black               │
│                                                 │
│  630 000 / 882 000 recebidos                    │
│  Em aberto: 252 000                             │
│                                                 │
│  ✅ Pedido → ✅ Enviado → 🟠 Parcial → ⚪ Concl. │
│                                                 │
│  ⏱ Há 2h 15min                                 │
└─────────────────────────────────────────────────┘
```

### 5.2 Card Elements

| Element | Position | Description |
|---------|----------|-------------|
| **PO number** | Top-left | `#3429` — bold, large font |
| **Status badge** | Top-right | Color-coded badge with stage label |
| **Route** | Below PO | `Viana 2 → Viana 1` — origin → destination |
| **Material name** | Center | Article/material description — max 1 line, truncated |
| **Quantity progress** | Below material | `received / ordered` with unit |
| **Open quantity** | Below qty | `Em aberto: N` — only if > 0 |
| **Mini timeline** | Bottom | 4–5 step visual indicator |
| **Age indicator** | Bottom-right | `Há Xh Ymin` since last event |

### 5.3 Card Sizing

| Context | Minimum Card Width | Minimum Card Height |
|---------|-------------------|---------------------|
| TV (40–55") | 400px | 180px |
| Desktop | 320px | 160px |
| Mobile (future) | full-width | 140px |

### 5.4 Card Visual States

| State | Border | Background | Effect |
|-------|--------|------------|--------|
| Normal | 1px solid border | Card background | — |
| Needs attention | 2px amber/orange border | Subtle amber tint | Optional pulse glow |
| Delayed / error | 2px red border | Subtle red tint | Optional pulse glow |
| Recently completed | 1px green border | Subtle green tint | Slight opacity fade |

---

## 6. Simplified Timeline Stages

### 6.1 Stage Definition

The Live Board uses a **simplified 5-stage business timeline**, not the detailed 10-event Operations timeline.

| # | Stage Code | Label PT | Label EN | Icon |
|---|-----------|----------|----------|------|
| 1 | `ORDERED` | Pedido | Ordered | 📄 Document |
| 2 | `SENT` | Enviado | Sent | 🚛 Truck |
| 3 | `RECEIVING` | Recebimento | Receiving | ⏳ Hourglass |
| 4 | `PARTIAL` | Parcial | Partial | 🟠 Half-circle |
| 5 | `COMPLETED` | Concluído | Completed | ✅ Check |

### 6.2 Mapping from Technical Events

| Live Stage | Technical Source / Rule |
|------------|------------------------|
| `ORDERED` | `PO_CREATED` exists (`T_Bestellungen.Status >= 1`) |
| `SENT` | Any of: `EDI_SYNCED` completed, `LOADING_ORDER` completed, `INHOUSE_DELIVERY` created |
| `RECEIVING` | `GR_CREATED` exists (pending) — transfer reached receipt stage but not completed |
| `PARTIAL` | PO status = `Parcialmente entregue` (Status 5) **OR** `receivedQuantity > 0 AND receivedQuantity < orderedQuantity` |
| `COMPLETED` | PO status = `Concluído` (Status 7/8) **OR** `receivedQuantity >= orderedQuantity` **OR** `openQuantity = 0` |

### 6.3 Step State in Mini Timeline

Each step in the mini timeline has one of three states:

| State | Visual | Meaning |
|-------|--------|---------|
| `done` | ✅ Green filled circle | Stage completed |
| `active` | 🟠 Amber/orange pulsing circle | Current stage |
| `pending` | ⚪ Grey empty circle | Not yet reached |

The steps are connected by a horizontal line that is:
- **Green** for completed segments
- **Grey** for pending segments

### 6.4 Labels to Avoid on Live Board

| ❌ Avoid | ✅ Use Instead |
|----------|---------------|
| `EDI_SYNCED` | Enviado |
| `GR_CREATED` | Recebimento |
| `GR_COMPLETED` | Concluído |
| `LOADING_ORDER` | Enviado |
| `PO_CREATED` | Pedido |
| `CALLOFF_CREATED` | (absorbed into Enviado) |
| `Journal` / `Abruf` | (not shown) |
| `T_Wareneingaenge` | (not shown) |
| `IdBestellung` | `#NNNN` (PO number) |

---

## 7. Status Colors and Icons

### 7.1 Color System

| Status | Color Name | Hex BG | Hex Text | CSS Variable Suggestion |
|--------|-----------|--------|----------|-------------------------|
| Pedido criado | Blue | `#dbeafe` | `#1e40af` | `--lb-status-ordered` |
| Enviado | Indigo | `#e0e7ff` | `#3730a3` | `--lb-status-sent` |
| Aguardando recebimento | Amber | `#fef3c7` | `#92400e` | `--lb-status-receiving` |
| Parcialmente recebido | Orange | `#ffedd5` | `#c2410c` | `--lb-status-partial` |
| Concluído | Green | `#dcfce7` | `#15803d` | `--lb-status-completed` |
| Cancelado / erro | Red | `#fee2e2` | `#991b1b` | `--lb-status-error` |

### 7.2 Icon System

| Status | Icon (Lucide) | Fallback Emoji |
|--------|--------------|----------------|
| Pedido criado | `FileText` | 📄 |
| Enviado | `Truck` | 🚛 |
| Aguardando recebimento | `Clock` / `Hourglass` | ⏳ |
| Parcialmente recebido | `CircleDot` / `PieChart` | 🟠 |
| Concluído | `CheckCircle2` | ✅ |
| Cancelado / erro | `AlertTriangle` | ⚠️ |
| Atrasado (delayed) | `AlertOctagon` | 🔴 |

### 7.3 Consistency with Existing Operations Module

These colors align with the existing `SEVERITY_BADGE` and `deriveListStage()` colors used in the Operations transfers list and drawer, ensuring visual consistency across the module.

---

## 8. Plant Context and Direction

### 8.1 Plant-Contextual Board

The Live Board is **plant-contextual** — it shows transfers relative to a specific plant.

**Route**: `/operations/live-board/:plant`

**Examples**:
- `/operations/live-board/VIANA1` — Board for Viana 1
- `/operations/live-board/VIANA2` — Board for Viana 2
- `/operations/live-board/VIANA3` — Board for Viana 3

### 8.2 Direction Logic

| Direction | Definition |
|-----------|-----------|
| **INBOUND** | Transfer where `destinationPlant` = current board plant |
| **OUTBOUND** | Transfer where `originPlant` = current board plant |

### 8.3 Known Plant Routes

Based on current business understanding:

| Route | Type | Pipeline |
|-------|------|----------|
| Viana 2 → Viana 1 | Standard logistics | STANDARD |
| Viana 1 → Viana 3 | Inhouse delivery | INHOUSE |
| Viana 1 → Viana 2 | Standard logistics (reverse) | STANDARD |
| Viana 3 → Viana 1 | Inhouse delivery (reverse) | INHOUSE |

**Per-plant view**:

| Board Plant | Inbound From | Outbound To |
|-------------|-------------|-------------|
| VIANA1 | VIANA2, VIANA3 | VIANA2, VIANA3 |
| VIANA2 | VIANA1 | VIANA1 |
| VIANA3 | VIANA1 | VIANA1 |

> [!WARNING]
> The exact plant routes need business confirmation (see Open Questions §13.1).

### 8.4 Direction Determination — Current State

The current AlplaPROD schema may not have explicit `originPlant` / `destinationPlant` fields per PO. Direction will need to be determined by:

**MVP Approximation**:
- The **board plant** is the plant whose database is queried
- The **partner plant** can be inferred from related address/plant references in `T_Bestellungen` or `T_Adressen`
- Direction is derived from the PO type (purchase = inbound, sales order = outbound) or the plant's known role

**Target Behavior** (future):
- The endpoint returns explicit `originPlant` and `destinationPlant` per transfer
- Direction is `INBOUND` if `destinationPlant === boardPlant`, `OUTBOUND` if `originPlant === boardPlant`

### 8.5 Proposed Direction Fields

```typescript
interface LiveBoardTransfer {
    originPlant: string;       // "VIANA2"
    destinationPlant: string;  // "VIANA1"
    direction: "INBOUND" | "OUTBOUND";
}
```

---

## 9. Proposed Endpoint

> [!IMPORTANT]
> This endpoint is **proposed only** — do not implement until approved.

### 9.1 Endpoint Definition

```
GET /api/operations/live-board?plant=VIANA1
```

### 9.2 Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `plant` | string | Yes | — | Plant code: `VIANA1`, `VIANA2`, `VIANA3` |
| `refreshSeconds` | int | No | `60` | Suggested refresh interval for the client |
| `maxInbound` | int | No | `8` | Maximum inbound transfers to return |
| `maxOutbound` | int | No | `8` | Maximum outbound transfers to return |
| `includeRecentlyCompleted` | bool | No | `true` | Include completed transfers within the window |
| `completedWindowHours` | int | No | `4` | Hours to keep completed transfers visible |

### 9.3 Response Shape

```json
{
  "plant": "VIANA1",
  "plantName": "Viana 1",
  "lastUpdated": "2026-06-01T10:42:15Z",
  "refreshSeconds": 60,
  "summary": {
    "inboundTotal": 5,
    "inboundActive": 3,
    "outboundTotal": 4,
    "outboundActive": 3,
    "attentionCount": 2,
    "completedRecentCount": 1
  },
  "inbound": [
    {
      "idBestellung": 3581,
      "journalNummer": "102425",
      "originPlant": "VIANA2",
      "originPlantName": "Viana 2",
      "destinationPlant": "VIANA1",
      "destinationPlantName": "Viana 1",
      "direction": "INBOUND",
      "materialName": "Purchased CM Cap 29mm Azul-Claro",
      "orderedQuantity": 4620000,
      "receivedQuantity": 0,
      "openQuantity": 4620000,
      "quantityUnit": "pcs",
      "currentStage": "RECEIVING",
      "currentStageLabel": "Aguardando recebimento",
      "statusColor": "warning",
      "isAttention": true,
      "attentionReason": "Aguardando recebimento há mais de 4 horas",
      "lastEventAt": "2026-06-01T09:38:00Z",
      "ageMinutes": 64,
      "steps": [
        { "code": "ORDERED", "label": "Pedido", "state": "done" },
        { "code": "SENT", "label": "Enviado", "state": "done" },
        { "code": "RECEIVING", "label": "Recebimento", "state": "active" },
        { "code": "COMPLETED", "label": "Concluído", "state": "pending" }
      ]
    }
  ],
  "outbound": [
    {
      "idBestellung": 3429,
      "journalNummer": "102380",
      "originPlant": "VIANA1",
      "originPlantName": "Viana 1",
      "destinationPlant": "VIANA3",
      "destinationPlantName": "Viana 3",
      "direction": "OUTBOUND",
      "materialName": "Purchased CM Cap 28mm CSDB Black",
      "orderedQuantity": 882000,
      "receivedQuantity": 630000,
      "openQuantity": 252000,
      "quantityUnit": "pcs",
      "currentStage": "PARTIAL",
      "currentStageLabel": "Parcialmente recebido",
      "statusColor": "warning",
      "isAttention": false,
      "attentionReason": null,
      "lastEventAt": "2026-06-01T08:15:00Z",
      "ageMinutes": 147,
      "steps": [
        { "code": "ORDERED", "label": "Pedido", "state": "done" },
        { "code": "SENT", "label": "Enviado", "state": "done" },
        { "code": "RECEIVING", "label": "Recebimento", "state": "done" },
        { "code": "PARTIAL", "label": "Parcial", "state": "active" },
        { "code": "COMPLETED", "label": "Concluído", "state": "pending" }
      ]
    }
  ],
  "queryDurationMs": 245
}
```

### 9.4 TypeScript Interface

```typescript
interface LiveBoardResponse {
    plant: string;
    plantName: string;
    lastUpdated: string;
    refreshSeconds: number;
    summary: LiveBoardSummary;
    inbound: LiveBoardTransfer[];
    outbound: LiveBoardTransfer[];
    queryDurationMs: number;
}

interface LiveBoardSummary {
    inboundTotal: number;
    inboundActive: number;
    outboundTotal: number;
    outboundActive: number;
    attentionCount: number;
    completedRecentCount: number;
}

interface LiveBoardTransfer {
    idBestellung: number;
    journalNummer: string | null;
    originPlant: string;
    originPlantName: string;
    destinationPlant: string;
    destinationPlantName: string;
    direction: "INBOUND" | "OUTBOUND";
    materialName: string | null;
    orderedQuantity: number | null;
    receivedQuantity: number | null;
    openQuantity: number | null;
    quantityUnit: string | null;
    currentStage: string;
    currentStageLabel: string;
    statusColor: string;
    isAttention: boolean;
    attentionReason: string | null;
    lastEventAt: string | null;
    ageMinutes: number | null;
    steps: LiveBoardStep[];
}

interface LiveBoardStep {
    code: string;
    label: string;
    state: "done" | "active" | "pending";
}
```

---

## 10. Data Source Strategy

### 10.1 Option A — Reuse Transfer List + Details/Timeline

**Approach**: Call existing list/details/timeline endpoints per transfer and transform in the frontend.

| Pros | Cons |
|------|------|
| Reuses existing code | Many queries per refresh (N+1 for details) |
| Lower initial backend effort | Slow for TV refresh with 10+ transfers |
| | Frontend does complex business interpretation |
| | Heavy client-side processing |

### 10.2 Option B — Dedicated Live Board Query (Recommended)

**Approach**: New optimized backend query that returns pre-simplified, pre-classified transfers in a single call.

| Pros | Cons |
|------|------|
| Optimized for TV — single query | More backend design work |
| Returns already simplified stages | New SQL query to maintain |
| Better performance (< 500ms target) | |
| Backend owns business interpretation | |
| Client is thin — just renders | |

### 10.3 Recommendation

> [!IMPORTANT]
> **Use Option B for MVP.** The Live Board has fundamentally different performance requirements than the admin drawer. A single optimized query returning pre-classified data is essential for 60-second auto-refresh on low-power kiosk devices.

Reuse existing mapping concepts and status rules from the Operations module:
- `OperationsStatusMapper` for PO status interpretation
- `OperationsPipelineDetector` for pipeline model
- `AlplaProdConnectionFactory` for plant connections
- `EntladeMenge` aggregation logic from Phase 7.1

But compose them into a single, purpose-built query in a new `OperationsLiveBoardService`.

---

## 11. Refresh Strategy

### 11.1 MVP: Polling

| Parameter | Default | Notes |
|-----------|---------|-------|
| Refresh interval | 60 seconds | Configurable via query param |
| Minimum interval | 30 seconds | Prevent abuse |
| Maximum interval | 300 seconds | Ensure data freshness |

The client uses `setInterval` with the server-provided `refreshSeconds` value.

### 11.2 Display Elements

| Element | Behavior |
|---------|----------|
| Last update time | `Última atualização: HH:MM:SS` — updates on each refresh |
| Countdown timer | `Próxima atualização em XXs` — animated countdown |
| Connection indicator | Green/amber/red dot next to header |

### 11.3 Stale Data Rules

| Condition | Visual | Action |
|-----------|--------|--------|
| Data < 2 minutes old | Green dot | Normal display |
| Data 2–5 minutes old | No change | Normal (within 5x refresh window) |
| Data 5–15 minutes old | Amber dot + warning banner | `⚠ Dados podem estar desatualizados` |
| Data > 15 minutes old or API error | Red dot + error banner | `🔴 Sem conexão — última atualização há Xmin` |
| API returns HTTP error | Red dot + error banner | Show last known data + error message |

### 11.4 Future: Server-Sent Events / WebSocket

For a future phase, consider upgrading to SSE or SignalR for push-based updates instead of polling. This eliminates unnecessary queries when nothing has changed and enables sub-second update latency.

---

## 12. Kiosk / TV Mode

### 12.1 Route

**Primary**: `/operations/live-board/VIANA1`

**Fullscreen param**: `/operations/live-board/VIANA1?fullscreen=true`

When `fullscreen=true` (or when accessed on a kiosk):
- Hide sidebar navigation
- Hide top navigation bar
- Use full viewport width and height
- Increase all font sizes by ~25%

### 12.2 Layout Rules for TV

| Rule | Value |
|------|-------|
| Minimum body font size | 18px |
| Card title font size | 22–24px |
| Status badge font size | 16px |
| Quantity font size | 20px |
| Mini timeline icon size | 24px |
| Background | Dark mode preferred (`#0f172a` or similar) for reduced eye strain |
| Contrast ratio | WCAG AAA (7:1) for primary text |
| Card spacing | 16–24px gaps |
| No manual scroll | All visible content fits in viewport |

### 12.3 Dark Mode

TV displays in warehouse/dock environments benefit from dark mode:
- Reduces glare in bright environments
- Better contrast for colored status indicators
- Lower power consumption on some displays
- Professional appearance

The Live Board should default to dark mode when `fullscreen=true`.

### 12.4 Auto-fit and Rotation

If more transfers exist than can fit on screen:

**Option A — Show top N with overflow indicator**:
```
+3 transferências em fila
```

**Option B — Auto-rotate pages** (future):
- Show page 1 for 15 seconds, page 2 for 15 seconds, etc.
- Smooth crossfade transition
- Page indicator dots at bottom

MVP: Use Option A. Option B for Phase Live 5.

---

## 13. Card Limits and Priority

### 13.1 Limits

| Parameter | Default | Configurable |
|-----------|---------|-------------|
| Max visible inbound cards | 6 | Via `maxInbound` query param |
| Max visible outbound cards | 6 | Via `maxOutbound` query param |
| Completed transfer window | 4 hours | Via `completedWindowHours` |

### 13.2 Priority Order

When more transfers exist than can be displayed, show in this priority order:

| Priority | Stage | Rationale |
|----------|-------|-----------|
| 1 | 🔴 Attention / Delayed | Needs immediate action |
| 2 | 🟠 Parcialmente recebido | Active, needs completion |
| 3 | ⏳ Aguardando recebimento | Pending, actionable |
| 4 | 🚛 Enviado | In transit |
| 5 | 📄 Pedido criado | Newest, not yet actionable |
| 6 | ✅ Concluído (recente) | Completed — lowest priority |

### 13.3 Completed Transfer Visibility

Completed transfers should remain visible for a configurable window to provide context:

| Option | Use Case |
|--------|----------|
| 4 hours | Quick dock operations — removes clutter fast |
| 8 hours | Full shift visibility |
| Current day | Day-level tracking |

**Default**: 4 hours. Configurable via `completedWindowHours`.

Recently completed transfers are visually de-emphasized:
- Slightly reduced opacity (0.7)
- Green-tinted background
- No attention indicators

---

## 14. Attention / Delay Detection

### 14.1 Attention Rules

| Rule | Condition | Attention Level |
|------|-----------|-----------------|
| Stuck in receiving | `currentStage = RECEIVING` AND `ageMinutes > 240` (4h) | ⚠ Warning |
| Long partial receipt | `currentStage = PARTIAL` AND `ageMinutes > 480` (8h) | ⚠ Warning |
| Created but not sent | `currentStage = ORDERED` AND `ageMinutes > 1440` (24h) | ⚠ Warning |
| Very old pending | Any non-completed stage AND `ageMinutes > 2880` (48h) | 🔴 Critical |

> [!WARNING]
> The attention thresholds above are initial proposals. The exact values need business validation (see Open Questions §16.6).

### 14.2 Visual Treatment

Attention transfers get:
- Colored left border (amber for warning, red for critical)
- `attentionReason` text displayed below the age indicator
- Priority boost in card ordering (shown first)
- Optional subtle pulse animation on the status badge

---

## 15. Security and Visibility

### 15.1 Information Shown on TV

| ✅ Show | ❌ Do NOT Show |
|---------|---------------|
| PO number (`#3429`) | Financial values (prices, totals) |
| Material name | Usernames / created by |
| Origin → Destination plant | Technical database/table names |
| Quantity (ordered/received/open) | Raw SQL or stack traces |
| Stage / status | Confidential supplier details |
| Age since last event | Technical error details |
| Unit of measure | Internal system IDs (except PO#) |

### 15.2 Authentication

| Aspect | Recommendation |
|--------|---------------|
| API endpoint | **Anonymous (`[AllowAnonymous]`)** — specific Live Board endpoint only |
| TV/kiosk access | Public access via `/operations/live-board/:plant` |
| Future: kiosk token | Consider kiosk tokens if further restrictions are needed |
| Public access | **Allowed only for Live Board** — all other endpoints remain protected |

**MVP approach**: The Live Board UI route (`/operations/live-board/:plant`) and the specific backend endpoint (`GET /api/operations/live-board`) are explicitly marked for anonymous access. No sensitive data (financials, usernames) is returned by this endpoint.

**Future approach**: Generate time-limited display tokens via admin panel, usable without interactive login, if further security controls are required.

### 15.3 Role Requirements

| Role | Access Level |
|------|-------------|
| System Administrator | Full access |
| Local Manager | Full access for assigned plants |
| Operations Viewer (future) | Read-only live board access |
| Display Token (future) | Read-only, no sidebar, no navigation |

---

## 16. Open Questions

### 16.1 Plant Routes

For each plant, what are the official inbound/outbound routes?

| Plant | Inbound From | Outbound To | Confirmed? |
|-------|-------------|-------------|------------|
| VIANA1 | VIANA2 | VIANA3 | ❓ Needs confirmation |
| VIANA1 | VIANA3 (returns?) | VIANA2 | ❓ Needs confirmation |
| VIANA2 | VIANA1 | VIANA1 | ❓ Needs confirmation |
| VIANA3 | VIANA1 | VIANA1 | ❓ Needs confirmation |

### 16.2 Completed Transfer Window

Should completed transfers remain visible for:
- 4 hours? (suggested default)
- 8 hours? (full shift)
- Current shift? (requires shift definition)
- Current day? (midnight reset)

### 16.3 Active vs Completed

Should the screen show:
- Only active transfers?
- Active + recently completed?
- Configurable per plant?

### 16.4 TV Authentication

How will the TV run the Live Board?
- Authenticated user session with auto-login?
- Kiosk user with limited permissions?
- Future display token (no interactive login)?
- Browser-saved credentials?

### 16.5 Alerts

Should the screen include sound/visual alert for delayed transfers?
- Audible beep on new attention item?
- Visual flash on status change?
- No alerts (passive display only)?

### 16.6 Delay Thresholds

What defines "delayed" for attention indicators?
- Waiting for receipt > X hours?
- Partial receipt for > X hours?
- Created but not sent for > X hours?
- Different thresholds per plant or pipeline?

### 16.7 Desktop Interaction

Should users be able to click a card on non-TV desktop mode to open the existing Operations drawer?
- If yes, the card becomes a link to `/operations/transfers` with the PO pre-selected
- If no, the Live Board is view-only everywhere

---

## 17. Implementation Phases

### Phase Live 1 — Design Document ✅ (Current)

Create this design document. No code changes.

**Deliverable**: `docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md`

---

### Phase Live 2 — Backend Live Board Endpoint

Create the dedicated endpoint:

```
GET /api/operations/live-board?plant=VIANA1
```

**Scope**:
- New `IOperationsLiveBoardService` / `OperationsLiveBoardService`
- New `OperationsLiveBoardQueryBuilder` — single optimized query
- New `OperationsLiveBoardDto` — pre-classified response
- Reuse `AlplaProdConnectionFactory`, `OperationsStatusMapper`, `OperationsPipelineDetector`
- Controller action in `OperationsController`
- DI registration

**Files**:
- `[NEW] OperationsLiveBoardDto.cs`
- `[NEW] IOperationsLiveBoardService.cs`
- `[NEW] OperationsLiveBoardService.cs`
- `[NEW] OperationsLiveBoardQueryBuilder.cs`
- `[MODIFY] OperationsController.cs`
- `[MODIFY] Program.cs`

---

### Phase Live 3 — Frontend TV Page

Create the Live Board screen:

```
/operations/live-board/:plant
```

**Scope**:
- New `OperationsLiveBoardPage.tsx`
- New route registration in `App.tsx`
- New navigation entry (optional — may be kiosk-only)
- Two-column layout with transfer cards
- Mini timeline component per card
- Auto-refresh with countdown
- Stale data detection

**Files**:
- `[NEW] OperationsLiveBoardPage.tsx`
- `[MODIFY] App.tsx`
- `[MODIFY] operations.types.ts`
- `[MODIFY] operationsApi.ts`

---

### Phase Live 4 — Kiosk / Fullscreen Mode

**Scope**:
- `?fullscreen=true` query parameter support
- Hide sidebar and top navigation
- Dark mode for TV
- Increased font sizes
- Connection status indicator
- Stale data warning banners

---

### Phase Live 5 — Refinement

**Scope**:
- Configurable card limits
- Completed transfer window
- Plant-specific direction confirmation
- Auto-rotation for overflow
- Attention thresholds tuning
- Optional desktop click-to-drawer integration
- Optional display token authentication

---

## 18. Related Documents

| Document | Purpose |
|----------|---------|
| [OPERATIONS_MODULE_TECHNICAL_DESIGN.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md) | Core Operations module design (timeline, status mapping, pipeline detection) |
| [OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md) | AlplaPROD schema discovery, entity relationships |
| [OPERATIONS_ENTITY_MAP.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_ENTITY_MAP.md) | Entity-to-table mapping |
