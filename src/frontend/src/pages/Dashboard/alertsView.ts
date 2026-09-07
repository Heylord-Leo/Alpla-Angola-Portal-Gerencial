// Dashboard V2 B8 — pure presentation helpers for canonical Alerts. These map server-provided codes and
// the server's signed `daysDelta` to display strings ONLY. No alert is derived, filtered, re-sorted or
// urgency-computed here — every alert (and its severity, plane, date, daysDelta) comes from the server
// (DashboardV2AlertDto). Node-vitest friendly (no DOM). Business Portuguese; backend codes are never
// renamed, only labelled.

// Domain code → PT label. The raw code (BUYER/FINANCE) never reaches the UI.
const DOMAIN_LABELS: Record<string, string> = {
  BUYER: 'Compras',
  FINANCE: 'Finanças',
};

export function alertDomainLabel(code: string): string {
  return DOMAIN_LABELS[code] ?? code;
}

// Plane code → label + chip color, using the same visual language as the other Dashboard plane chips
// (Pessoal green, Compartilhado amber, Gerencial slate). Gerencial is informational — the server marks
// its rows non-navigable, so the chip never implies an action here.
export interface AlertPlaneMeta {
  label: string;
  color: string;
}

const PLANE_META: Record<string, AlertPlaneMeta> = {
  PESSOAL: { label: 'Pessoal', color: '#2f6f4f' },
  COMPARTILHADO: { label: 'Compartilhado', color: '#a15c1e' },
  GERENCIAL: { label: 'Gerencial', color: '#3b5069' },
};

export function alertPlaneMeta(code: string): AlertPlaneMeta {
  return PLANE_META[code] ?? { label: code, color: '#3b5069' };
}

// Severity code → label + color. The label text is always shown alongside an icon, so severity is never
// conveyed by color alone. Colors match the established Dashboard V2 severity palette (critical red,
// attention amber) used by the Finance section.
export interface AlertSeverityMeta {
  label: string;
  color: string;
  isCritical: boolean;
}

export function alertSeverityMeta(code: string): AlertSeverityMeta {
  return code === 'CRITICAL'
    ? { label: 'Crítico', color: '#dc2626', isCritical: true }
    : { label: 'Atenção', color: '#d97706', isCritical: false };
}

// Format the server's signed daysDelta into deterministic PT wording. The frontend does NOT recompute
// urgency from the current date — it only formats the value the server already resolved.
//   daysDelta < 0  → "Vencido há X dia(s)"
//   daysDelta == 0 → "Vence hoje"
//   daysDelta == 1 → "Vence amanhã"
//   daysDelta >= 2 → "Vence em X dias"
export function alertUrgencyText(daysDelta: number): string {
  if (daysDelta < 0) {
    const n = Math.abs(daysDelta);
    return n === 1 ? 'Vencido há 1 dia' : `Vencido há ${n} dias`;
  }
  if (daysDelta === 0) return 'Vence hoje';
  if (daysDelta === 1) return 'Vence amanhã';
  return `Vence em ${daysDelta} dias`;
}

// Concise headline from the summary counts (NEVER from the visible list, which may be truncated).
// e.g. "3 críticos · 5 em atenção" — singular "crítico" when the count is 1.
export function alertSummaryText(criticalCount: number, attentionCount: number): string {
  const criticos = `${criticalCount} ${criticalCount === 1 ? 'crítico' : 'críticos'}`;
  const atencao = `${attentionCount} em atenção`;
  return `${criticos} · ${atencao}`;
}

// Dashboard preview footer (compact): honest "N of total" for the previewed rows. `shown` is the number
// of preview rows actually rendered (≤ 6); `total` is the full active population. Never implies the
// preview is complete.
export function alertPreviewFooterText(shown: number, totalAlertCount: number): string {
  return `Exibindo ${shown} de ${totalAlertCount} alertas ativos.`;
}

// Drawer backend-truncation notice — shown ONLY when the server capped the returned list. States the
// honest returned/total ratio so the drawer never implies all `total` rows were loaded.
export function alertBackendTruncationText(displayedAlertCount: number, totalAlertCount: number): string {
  return `A API retornou os ${displayedAlertCount} alertas mais prioritários de ${totalAlertCount} ativos.`;
}
