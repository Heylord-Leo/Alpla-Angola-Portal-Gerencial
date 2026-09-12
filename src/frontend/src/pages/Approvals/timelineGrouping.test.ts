import { describe, it, expect } from 'vitest';
import { groupTimelineEvents, localizeComment, minuteBucket, GROUPABLE_ACTION } from './tabs/timelineGrouping';
import type { ApprovalTimelineEvent } from '../../types/approvalHistory';

// Node-env vitest — pure-function tests for the v2.244.0 Phase-2 timeline grouping/localization.
// Drawer wiring is covered by source guards in ApprovalTimelineDrawer.polish.test.ts.

let seq = 0;
function ev(p: Partial<ApprovalTimelineEvent>): ApprovalTimelineEvent {
  return {
    id: p.id ?? `e${seq++}`,
    actionTaken: p.actionTaken ?? GROUPABLE_ACTION,
    actionLabel: p.actionLabel ?? 'Candidatos submetidos',
    approvalLevel: p.approvalLevel ?? null,
    decision: p.decision ?? null,
    isDecision: p.isDecision ?? false,
    actorUserId: p.actorUserId ?? 'u1',
    actorName: p.actorName ?? 'Celestina Bernardo',
    comment: p.comment ?? null,
    previousStatusCode: p.previousStatusCode ?? null,
    newStatusCode: p.newStatusCode ?? null,
    batchNumber: p.batchNumber ?? 1,
    createdAtUtc: p.createdAtUtc ?? '2026-08-31T13:45:53.000Z',
  };
}

const candidate = (over: Partial<ApprovalTimelineEvent> = {}) => ev({ actionTaken: GROUPABLE_ACTION, ...over });

describe('groupTimelineEvents — grouping rule (A–F, K)', () => {
  it('A: consecutive same actor/action/lote/minute candidate events group into one node', () => {
    const nodes = groupTimelineEvents([
      candidate({ id: 'a', comment: 'Item #5' }),
      candidate({ id: 'b', comment: 'Item #6' }),
      candidate({ id: 'c', comment: 'Item #7' }),
    ]);
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('group');
    if (nodes[0].kind === 'group') expect(nodes[0].events).toHaveLength(3);
  });

  it('F: the grouped item count equals the number of collapsed events', () => {
    const nodes = groupTimelineEvents([candidate(), candidate(), candidate(), candidate(), candidate()]);
    expect(nodes).toHaveLength(1);
    if (nodes[0].kind === 'group') expect(nodes[0].events).toHaveLength(5);
  });

  it('B: different lote does NOT group', () => {
    const nodes = groupTimelineEvents([
      candidate({ batchNumber: 1 }),
      candidate({ batchNumber: 2 }),
    ]);
    expect(nodes.every(n => n.kind === 'single')).toBe(true);
    expect(nodes).toHaveLength(2);
  });

  it('C: different actor does NOT group', () => {
    const nodes = groupTimelineEvents([
      candidate({ actorUserId: 'u1' }),
      candidate({ actorUserId: 'u2' }),
    ]);
    expect(nodes).toHaveLength(2);
    expect(nodes.every(n => n.kind === 'single')).toBe(true);
  });

  it('D: different timestamp minute does NOT group', () => {
    const nodes = groupTimelineEvents([
      candidate({ createdAtUtc: '2026-08-31T13:45:53.000Z' }),
      candidate({ createdAtUtc: '2026-08-31T13:47:10.000Z' }),
    ]);
    expect(nodes).toHaveLength(2);
    expect(nodes.every(n => n.kind === 'single')).toBe(true);
  });

  it('a lone candidate event stays a single (nothing to collapse)', () => {
    const nodes = groupTimelineEvents([candidate({ id: 'solo' })]);
    expect(nodes).toHaveLength(1);
    expect(nodes[0].kind).toBe('single');
  });

  it('K: order is preserved and no event is dropped (flatten === input order)', () => {
    const input = [
      ev({ id: '1', actionTaken: 'CREATED', actionLabel: 'Pedido criado' }),
      ev({ id: '2', actionTaken: 'SUBMIT', actionLabel: 'Submetido' }),
      candidate({ id: '3' }),
      candidate({ id: '4' }),
      ev({ id: '5', actionTaken: 'BATCH_AREA_APPROVED', isDecision: true, decision: 'APPROVED', approvalLevel: 'AREA', actionLabel: 'Aprovação de Área', createdAtUtc: '2026-08-31T14:00:00.000Z' }),
    ];
    const nodes = groupTimelineEvents(input);
    const flatIds = nodes.flatMap(n => n.kind === 'single' ? [n.event.id] : n.events.map(e => e.id));
    expect(flatIds).toEqual(['1', '2', '3', '4', '5']);
  });
});

describe('groupTimelineEvents — decisions/materials never group (E, J)', () => {
  it('E: approval decisions never group even when consecutive/equal', () => {
    const nodes = groupTimelineEvents([
      ev({ id: 'd1', actionTaken: 'BATCH_AREA_APPROVED', isDecision: true, decision: 'APPROVED', approvalLevel: 'AREA' }),
      ev({ id: 'd2', actionTaken: 'BATCH_FINAL_APPROVED', isDecision: true, decision: 'APPROVED', approvalLevel: 'FINAL' }),
    ]);
    expect(nodes.every(n => n.kind === 'single')).toBe(true);
  });

  it('J: PAYMENT Area then Final approvals remain individual', () => {
    const nodes = groupTimelineEvents([
      ev({ id: 'c', actionTaken: 'CREATED', actionLabel: 'Pedido criado' }),
      ev({ id: 's', actionTaken: 'SUBMIT', actionLabel: 'Submetido para aprovação' }),
      ev({ id: 'area', actionTaken: 'APPROVE', isDecision: true, decision: 'APPROVED', approvalLevel: 'AREA', actionLabel: 'Aprovação de Área', previousStatusCode: 'WAITING_AREA_APPROVAL', newStatusCode: 'WAITING_FINAL_APPROVAL' }),
      ev({ id: 'final', actionTaken: 'APPROVE', isDecision: true, decision: 'APPROVED', approvalLevel: 'FINAL', actionLabel: 'Aprovação Final', previousStatusCode: 'WAITING_FINAL_APPROVAL', newStatusCode: 'APPROVED' }),
    ]);
    expect(nodes).toHaveLength(4);
    expect(nodes.every(n => n.kind === 'single')).toBe(true);
  });

  it('a decision between two candidate runs keeps them separate', () => {
    const nodes = groupTimelineEvents([
      candidate({ id: 'a' }), candidate({ id: 'b' }),
      ev({ id: 'dec', actionTaken: 'BATCH_AREA_APPROVED', isDecision: true, decision: 'APPROVED', approvalLevel: 'AREA', createdAtUtc: '2026-08-31T13:45:53.000Z' }),
      candidate({ id: 'c' }), candidate({ id: 'd' }),
    ]);
    // group, single(decision), group
    expect(nodes.map(n => n.kind)).toEqual(['group', 'single', 'group']);
  });
});

describe('groupTimelineEvents — raw comments preserved (G)', () => {
  it('G: every original comment survives inside the group', () => {
    const comments = ['Item #5 ABRAÇADEIRA', 'Item #6 COLA RÁPIDA', 'Item #7 PARAFUSO'];
    const nodes = groupTimelineEvents(comments.map((c, i) => candidate({ id: `x${i}`, comment: c })));
    expect(nodes).toHaveLength(1);
    if (nodes[0].kind === 'group') {
      expect(nodes[0].events.map(e => e.comment)).toEqual(comments);
    }
  });
});

describe('localizeComment — known system messages only (H, I)', () => {
  it('H: known English system comments get a PT presentation, raw preserved', () => {
    const p = localizeComment('Request created as Draft.');
    expect(p).not.toBeNull();
    expect(p!.text).toBe('Pedido criado como rascunho.');
    expect(p!.raw).toBe('Request created as Draft.');
    expect(p!.localized).toBe(true);

    const p2 = localizeComment('Request created and submitted for quotation.');
    expect(p2!.text).toBe('Pedido criado e submetido para cotação.');
    expect(p2!.localized).toBe(true);
  });

  it('I: arbitrary free-text user comments are returned unchanged', () => {
    const raw = 'Rever preço do item 3, fornecedor caro.';
    const p = localizeComment(raw);
    expect(p!.text).toBe(raw);
    expect(p!.raw).toBe(raw);
    expect(p!.localized).toBe(false);
  });

  it('null comment yields null', () => {
    expect(localizeComment(null)).toBeNull();
  });
});

describe('minuteBucket', () => {
  it('same minute → same bucket; next minute → different bucket', () => {
    expect(minuteBucket('2026-08-31T13:45:53.000Z')).toBe(minuteBucket('2026-08-31T13:45:12.000Z'));
    expect(minuteBucket('2026-08-31T13:45:53.000Z')).not.toBe(minuteBucket('2026-08-31T13:46:00.000Z'));
  });
});
