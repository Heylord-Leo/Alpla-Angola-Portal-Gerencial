// Dashboard V2 B6 — pure presentation helpers for the Operational Pipeline. These map server values to
// display strings and group metadata ONLY. No pipeline membership, counts, or actionability are computed
// here — every number comes from the server (OperationalPipelineStageDto). Node-vitest friendly (no DOM).

import type { OperationalPipelineStageDto } from '../../types/dashboardV2';

// Canonical entity unit → PT singular/plural. One place, never scattered across components.
const UNIT_WORDS: Record<string, [string, string]> = {
  REQUEST: ['pedido', 'pedidos'],
  APPROVAL_BATCH: ['lote', 'lotes'],
  PO_GROUP: ['grupo', 'grupos'],
  LINE_ITEM: ['item', 'itens'],
};

export function entityUnit(entityType: string, count: number): string {
  const pair = UNIT_WORDS[entityType];
  if (!pair) return '';
  return count === 1 ? pair[0] : pair[1];
}

/** Primary line, e.g. "2 lotes", "70 grupos", "25 pedidos". */
export function primaryEntityText(stage: Pick<OperationalPipelineStageDto, 'entityType' | 'entityCount'>): string {
  const unit = entityUnit(stage.entityType, stage.entityCount);
  return unit ? `${stage.entityCount} ${unit}` : `${stage.entityCount}`;
}

/** Secondary "· N pedidos" line — omitted for REQUEST grain to avoid "25 pedidos · 25 pedidos". */
export function secondaryRequestText(
  stage: Pick<OperationalPipelineStageDto, 'entityType' | 'requestCount'>
): string | null {
  if (stage.entityType === 'REQUEST') return null;
  return `${stage.requestCount} ${stage.requestCount === 1 ? 'pedido' : 'pedidos'}`;
}

// Visual domain groups (ordered). Reajuste already arrives as domain APROVACOES from the server;
// Documentação (DOCUMENTACAO) and Concluído (CONCLUSAO) are merged into one "Conclusão" group.
export interface PipelineDomainGroup {
  key: string;
  label: string;
  domains: string[];
}

export const PIPELINE_DOMAIN_GROUPS: PipelineDomainGroup[] = [
  { key: 'preparacao', label: 'Preparação', domains: ['PREPARACAO'] },
  { key: 'compras', label: 'Compras', domains: ['COMPRAS'] },
  { key: 'aprovacoes', label: 'Aprovações', domains: ['APROVACOES'] },
  { key: 'po', label: 'P.O.', domains: ['PO'] },
  { key: 'financas', label: 'Finanças', domains: ['FINANCAS'] },
  { key: 'recebimento', label: 'Recebimento', domains: ['RECEBIMENTO'] },
  { key: 'conclusao', label: 'Conclusão', domains: ['DOCUMENTACAO', 'CONCLUSAO'] },
];

/** Group the server stages into the ordered visual groups; drops empty groups. Stages keep server order. */
export function groupStages(stages: OperationalPipelineStageDto[]): Array<{ group: PipelineDomainGroup; stages: OperationalPipelineStageDto[] }> {
  const sorted = [...stages].sort((a, b) => a.sortOrder - b.sortOrder);
  return PIPELINE_DOMAIN_GROUPS
    .map((group) => ({ group, stages: sorted.filter((s) => group.domains.includes(s.domain)) }))
    .filter((g) => g.stages.length > 0);
}

/** The Reajuste stage gets a loop/retry affordance (it is not a strictly-forward step). */
export function isAdjustmentStage(stage: Pick<OperationalPipelineStageDto, 'stage'>): boolean {
  return stage.stage === 'ADJUSTMENT';
}
