// Dashboard V2 B9.5 — pure presentation helpers for canonical Stage Aging ("Gargalos"). These format the
// server's counts into managerial wording ONLY: they never re-derive severity, never compute age, and never
// coerce a null (thresholdless severity / unknown age) into 0. Node-vitest friendly (no DOM). Age wording
// is strictly time-in-CURRENT-STAGE — request-age language is never produced.

import type { DashboardV2StageAgingStageDto } from '../../types/dashboardV2';

// Grain → PT unit (singular/plural). APPROVAL_BATCH = lote(s); PO_GROUP = grupo(s). Never "pedido" for the grain.
const UNIT: Record<string, [string, string]> = {
  APPROVAL_BATCH: ['lote', 'lotes'],
  PO_GROUP: ['grupo', 'grupos'],
};

export function entityUnit(entityType: string, count: number): string {
  const pair = UNIT[entityType];
  if (!pair) return '';
  return count === 1 ? pair[0] : pair[1];
}

/** "12 lotes · 10 pedidos" — grain count plus distinct request count. */
export function entityCountText(stage: Pick<DashboardV2StageAgingStageDto, 'entityType' | 'entityCount' | 'requestCount'>): string {
  const unit = entityUnit(stage.entityType, stage.entityCount);
  const grain = unit ? `${stage.entityCount} ${unit}` : `${stage.entityCount}`;
  return `${grain} · ${stage.requestCount} pedido${stage.requestCount === 1 ? '' : 's'}`;
}

/** Oldest time-in-CURRENT-STAGE. null → unknown; 0 → entered today; else "há N dia(s) nesta etapa". */
export function oldestAgeText(oldestAgeDays: number | null): string {
  if (oldestAgeDays === null) return 'Idade não disponível';
  if (oldestAgeDays === 0) return 'Entrou nesta etapa hoje';
  return oldestAgeDays === 1 ? 'Há 1 dia nesta etapa' : `Há ${oldestAgeDays} dias nesta etapa`;
}

/** A stage carries severity only when the server gave it a threshold profile (Approval/PO/Receiving). */
export function isThresholded(stage: Pick<DashboardV2StageAgingStageDto, 'thresholdProfile'>): boolean {
  return stage.thresholdProfile !== null;
}

export type SegmentTone = 'critical' | 'attention' | 'normal' | 'known' | 'unknown';
export interface CompositionSegment { tone: SegmentTone; count: number; }

// Composition WITHIN one stage (never relative to other stages). Thresholded stages split known-age into
// critical/attention/normal; thresholdless stages show a single neutral "known" band. Unknown is always a
// distinct neutral band. Only non-zero segments are returned. Widths are the caller's job (raw counts here).
export function compositionSegments(stage: DashboardV2StageAgingStageDto): CompositionSegment[] {
  const segs: CompositionSegment[] = [];
  if (isThresholded(stage)) {
    if ((stage.criticalCount ?? 0) > 0) segs.push({ tone: 'critical', count: stage.criticalCount! });
    if ((stage.attentionCount ?? 0) > 0) segs.push({ tone: 'attention', count: stage.attentionCount! });
    if ((stage.normalCount ?? 0) > 0) segs.push({ tone: 'normal', count: stage.normalCount! });
  } else if (stage.knownAgeEntityCount > 0) {
    segs.push({ tone: 'known', count: stage.knownAgeEntityCount });
  }
  if (stage.unknownAgeEntityCount > 0) segs.push({ tone: 'unknown', count: stage.unknownAgeEntityCount });
  return segs;
}

// The composition bar is rendered ONLY when it carries meaningful SEVERITY composition — i.e. a
// threshold-enabled stage that has at least one known-age (classified) entity. Suppressed when the bar
// would be a single solid band with no severity meaning: an all-unknown stage (only the unknown band) or a
// thresholdless Finance/Documentation stage (only a neutral "known" band). Severity color must mean something.
export function hasMeaningfulComposition(stage: Pick<DashboardV2StageAgingStageDto, 'thresholdProfile' | 'knownAgeEntityCount'>): boolean {
  return stage.thresholdProfile !== null && stage.knownAgeEntityCount > 0;
}

/** Honest composition wording. Thresholded → severity breakdown; thresholdless → known/unknown only. */
export function compositionText(stage: DashboardV2StageAgingStageDto): string {
  const parts: string[] = [];
  if (isThresholded(stage)) {
    const c = stage.criticalCount ?? 0, a = stage.attentionCount ?? 0, n = stage.normalCount ?? 0;
    if (c > 0) parts.push(`${c} ${c === 1 ? 'crítico' : 'críticos'}`);
    if (a > 0) parts.push(`${a} em atenção`);
    if (n > 0) parts.push(`${n} ${n === 1 ? 'normal' : 'normais'}`);
  } else if (stage.knownAgeEntityCount > 0) {
    parts.push(`${stage.knownAgeEntityCount} com idade conhecida`);
  }
  if (stage.unknownAgeEntityCount > 0) parts.push(`${stage.unknownAgeEntityCount} sem idade disponível`);
  return parts.join(' · ');
}

// Bottleneck risk ranking (presentation only — the server array is NEVER mutated; this returns a copy).
// Critical desc → Attention desc → oldest age desc → pipeline sort. Null severity/age behave as non-severity,
// never as an error.
export function rankByBottleneck(stages: readonly DashboardV2StageAgingStageDto[]): DashboardV2StageAgingStageDto[] {
  return [...stages].sort((a, b) =>
    (b.criticalCount ?? 0) - (a.criticalCount ?? 0)
    || (b.attentionCount ?? 0) - (a.attentionCount ?? 0)
    || (b.oldestAgeDays ?? -1) - (a.oldestAgeDays ?? -1)
    || a.sortOrder - b.sortOrder);
}
