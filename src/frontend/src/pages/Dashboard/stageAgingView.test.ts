import { describe, it, expect } from 'vitest';
import {
  entityUnit, entityCountText, oldestAgeText, isThresholded, hasMeaningfulComposition,
  compositionText, compositionSegments, rankByBottleneck,
} from './stageAgingView';
import type { DashboardV2StageAgingStageDto } from '../../types/dashboardV2';

function stage(p: Partial<DashboardV2StageAgingStageDto>): DashboardV2StageAgingStageDto {
  return {
    domain: 'PO', stageCode: 'PO_WAITING', label: 'Aguardando P.O.', entityType: 'PO_GROUP', sortOrder: 50,
    entityCount: 0, requestCount: 0, knownAgeEntityCount: 0, unknownAgeEntityCount: 0,
    normalCount: null, attentionCount: null, criticalCount: null,
    oldestStageEnteredAtUtc: null, oldestAgeDays: null, thresholdProfile: null, targetPath: null, canNavigate: false,
    ...p,
  };
}
const thr = { attentionAfterDays: 3, criticalAfterDays: 7, isFormalSla: false };

describe('stageAgingView — grain wording', () => {
  it('uses lote(s) for batches and grupo(s) for groups — never "pedido" for the grain', () => {
    expect(entityUnit('APPROVAL_BATCH', 1)).toBe('lote');
    expect(entityUnit('APPROVAL_BATCH', 2)).toBe('lotes');
    expect(entityUnit('PO_GROUP', 1)).toBe('grupo');
    expect(entityUnit('PO_GROUP', 3)).toBe('grupos');
  });
  it('formats grain + distinct request count', () => {
    expect(entityCountText(stage({ entityType: 'APPROVAL_BATCH', entityCount: 12, requestCount: 10 }))).toBe('12 lotes · 10 pedidos');
    expect(entityCountText(stage({ entityType: 'PO_GROUP', entityCount: 1, requestCount: 1 }))).toBe('1 grupo · 1 pedido');
  });
});

describe('stageAgingView — age wording (time-in-current-stage, never request age)', () => {
  it('null → unknown, 0 → today, 1 → 1 dia, N → N dias', () => {
    expect(oldestAgeText(null)).toBe('Idade não disponível');
    expect(oldestAgeText(0)).toBe('Entrou nesta etapa hoje');
    expect(oldestAgeText(1)).toBe('Há 1 dia nesta etapa');
    expect(oldestAgeText(9)).toBe('Há 9 dias nesta etapa');
  });
  it('never produces request-age phrasing', () => {
    for (const n of [null, 0, 1, 9]) {
      const t = oldestAgeText(n as number | null);
      expect(t).not.toMatch(/pedido/i);
      expect(t).not.toMatch(/criado/i);
    }
  });
});

describe('stageAgingView — composition (thresholded)', () => {
  const s = stage({ thresholdProfile: thr, knownAgeEntityCount: 6, criticalCount: 2, attentionCount: 1, normalCount: 3, unknownAgeEntityCount: 2, entityCount: 8 });
  it('is thresholded', () => expect(isThresholded(s)).toBe(true));
  it('text lists severity + unknown, with singular/plural', () => {
    expect(compositionText(s)).toBe('2 críticos · 1 em atenção · 3 normais · 2 sem idade disponível');
    expect(compositionText(stage({ thresholdProfile: thr, criticalCount: 1, attentionCount: 0, normalCount: 1, knownAgeEntityCount: 2 })))
      .toBe('1 crítico · 1 normal');
  });
  it('segments split known into severity + a distinct unknown band', () => {
    const segs = compositionSegments(s);
    expect(segs.map(x => x.tone)).toEqual(['critical', 'attention', 'normal', 'unknown']);
    expect(segs.find(x => x.tone === 'unknown')!.count).toBe(2);
  });
});

describe('stageAgingView — composition (thresholdless: Finance/Documentation)', () => {
  const s = stage({ thresholdProfile: null, knownAgeEntityCount: 106, unknownAgeEntityCount: 0, entityCount: 106 });
  it('is NOT thresholded', () => expect(isThresholded(s)).toBe(false));
  it('shows known/unknown only — never "0 críticos" / "0 em atenção" / "normal"', () => {
    const t = compositionText(s);
    expect(t).toBe('106 com idade conhecida');
    expect(t).not.toMatch(/crítico/i);
    expect(t).not.toMatch(/atenção/i);
    expect(t).not.toMatch(/normal/i);
  });
  it('segments are a single neutral known band (+ unknown if any) — no severity tones', () => {
    expect(compositionSegments(s).map(x => x.tone)).toEqual(['known']);
    expect(compositionSegments(stage({ thresholdProfile: null, knownAgeEntityCount: 3, unknownAgeEntityCount: 7 })).map(x => x.tone))
      .toEqual(['known', 'unknown']);
  });
});

describe('stageAgingView — composition bar is rendered only when it means severity', () => {
  it('keeps the bar for a thresholded stage with classified entities', () => {
    expect(hasMeaningfulComposition(stage({ thresholdProfile: thr, knownAgeEntityCount: 12 }))).toBe(true);
    expect(hasMeaningfulComposition(stage({ thresholdProfile: thr, knownAgeEntityCount: 3, unknownAgeEntityCount: 7 }))).toBe(true); // mixed
  });
  it('suppresses the bar for an all-unknown thresholded stage (only an unknown band)', () => {
    expect(hasMeaningfulComposition(stage({ thresholdProfile: thr, knownAgeEntityCount: 0, unknownAgeEntityCount: 49 }))).toBe(false);
  });
  it('suppresses the bar for thresholdless stages (only a neutral known band)', () => {
    expect(hasMeaningfulComposition(stage({ thresholdProfile: null, knownAgeEntityCount: 106 }))).toBe(false);
    expect(hasMeaningfulComposition(stage({ thresholdProfile: null, knownAgeEntityCount: 0, unknownAgeEntityCount: 8 }))).toBe(false);
  });
});

describe('stageAgingView — unknown is first-class, never normal', () => {
  it('all-unknown stage: no severity band, only unknown', () => {
    const s = stage({ thresholdProfile: thr, knownAgeEntityCount: 0, unknownAgeEntityCount: 49, entityCount: 49, criticalCount: 0, attentionCount: 0, normalCount: 0 });
    expect(compositionText(s)).toBe('49 sem idade disponível');
    expect(compositionSegments(s).map(x => x.tone)).toEqual(['unknown']);
    expect(oldestAgeText(s.oldestAgeDays)).toBe('Idade não disponível');
  });
});

describe('stageAgingView — bottleneck ranking (pure, no mutation)', () => {
  it('critical desc → attention desc → oldest desc → sort order, and returns a copy', () => {
    const input = [
      stage({ stageCode: 'A', sortOrder: 60, criticalCount: 0, attentionCount: 5, oldestAgeDays: 10 }),
      stage({ stageCode: 'B', sortOrder: 30, criticalCount: 2, attentionCount: 0, oldestAgeDays: 3 }),
      stage({ stageCode: 'C', sortOrder: 50, criticalCount: 2, attentionCount: 9, oldestAgeDays: 1 }),
    ];
    const ranked = rankByBottleneck(input);
    expect(ranked.map(s => s.stageCode)).toEqual(['C', 'B', 'A']);
    expect(input.map(s => s.stageCode)).toEqual(['A', 'B', 'C']); // input not mutated
  });
  it('null severity/age behave as non-severity, not an error', () => {
    const ranked = rankByBottleneck([
      stage({ stageCode: 'FIN', criticalCount: null, attentionCount: null, oldestAgeDays: 20, sortOrder: 61 }),
      stage({ stageCode: 'PO', criticalCount: 1, attentionCount: 0, oldestAgeDays: 2, sortOrder: 50 }),
    ]);
    expect(ranked[0].stageCode).toBe('PO'); // 1 critical outranks a thresholdless 20-day stage
  });
});
