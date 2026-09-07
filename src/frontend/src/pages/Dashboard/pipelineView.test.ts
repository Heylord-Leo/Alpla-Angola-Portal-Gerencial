import { describe, it, expect } from 'vitest';
import { entityUnit, primaryEntityText, secondaryRequestText, groupStages, isAdjustmentStage } from './pipelineView';
import type { OperationalPipelineStageDto } from '../../types/dashboardV2';

const stage = (over: Partial<OperationalPipelineStageDto> = {}): OperationalPipelineStageDto => ({
  domain: 'COMPRAS', stage: 'NEEDS_QUOTATION', label: 'Sem cotação', entityType: 'REQUEST',
  entityCount: 1, requestCount: 1, sortOrder: 20, targetPath: null, canOverlap: true, ...over,
});

describe('pipelineView', () => {
  it('entityUnit is singular/plural per canonical entity type', () => {
    expect(entityUnit('REQUEST', 1)).toBe('pedido');
    expect(entityUnit('REQUEST', 2)).toBe('pedidos');
    expect(entityUnit('APPROVAL_BATCH', 1)).toBe('lote');
    expect(entityUnit('APPROVAL_BATCH', 3)).toBe('lotes');
    expect(entityUnit('PO_GROUP', 1)).toBe('grupo');
    expect(entityUnit('PO_GROUP', 70)).toBe('grupos');
    expect(entityUnit('LINE_ITEM', 1)).toBe('item');
    expect(entityUnit('LINE_ITEM', 5)).toBe('itens');
  });

  it('primaryEntityText renders count + unit', () => {
    expect(primaryEntityText(stage({ entityType: 'APPROVAL_BATCH', entityCount: 2 }))).toBe('2 lotes');
    expect(primaryEntityText(stage({ entityType: 'PO_GROUP', entityCount: 70 }))).toBe('70 grupos');
    expect(primaryEntityText(stage({ entityType: 'REQUEST', entityCount: 25 }))).toBe('25 pedidos');
  });

  it('secondaryRequestText is omitted for REQUEST grain (avoids "25 pedidos · 25 pedidos")', () => {
    expect(secondaryRequestText(stage({ entityType: 'REQUEST', requestCount: 25 }))).toBeNull();
    expect(secondaryRequestText(stage({ entityType: 'PO_GROUP', requestCount: 1 }))).toBe('1 pedido');
    expect(secondaryRequestText(stage({ entityType: 'APPROVAL_BATCH', requestCount: 14 }))).toBe('14 pedidos');
  });

  it('groupStages orders groups, merges DOCUMENTACAO+CONCLUSAO, drops empty groups', () => {
    const stages = [
      stage({ domain: 'CONCLUSAO', stage: 'COMPLETED', sortOrder: 90 }),
      stage({ domain: 'COMPRAS', stage: 'NEEDS_QUOTATION', sortOrder: 20 }),
      stage({ domain: 'DOCUMENTACAO', stage: 'DOCUMENTATION', sortOrder: 80 }),
      stage({ domain: 'APROVACOES', stage: 'AREA_APPROVAL', sortOrder: 30 }),
    ];
    const groups = groupStages(stages);
    const keys = groups.map((g) => g.group.key);
    expect(keys).toEqual(['compras', 'aprovacoes', 'conclusao']); // preparacao/po/financas/recebimento empty → dropped
    // Conclusão merges Documentação + Concluído, in sortOrder.
    const conclusao = groups.find((g) => g.group.key === 'conclusao')!;
    expect(conclusao.stages.map((s) => s.stage)).toEqual(['DOCUMENTATION', 'COMPLETED']);
  });

  it('isAdjustmentStage flags only the Reajuste stage', () => {
    expect(isAdjustmentStage(stage({ stage: 'ADJUSTMENT' }))).toBe(true);
    expect(isAdjustmentStage(stage({ stage: 'AREA_APPROVAL' }))).toBe(false);
  });
});
