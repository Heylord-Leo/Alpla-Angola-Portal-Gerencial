import type { ApprovalTimelineEvent } from '../../../types/approvalHistory';

// v2.244.0 Approval Center V2 — Phase 2 timeline polish. Pure, presentation-only helpers for the
// audit-timeline drawer. Grouping NEVER reorders, discards, or mutates events — it only collapses a
// run of equivalent BATCH_CANDIDATES_SUBMITTED context events into one node whose original events are
// kept verbatim for the expanded view.

// Only this one context action may be grouped. Every material decision/return/resubmit stays individual.
export const GROUPABLE_ACTION = 'BATCH_CANDIDATES_SUBMITTED';

export type TimelineNode =
  | { kind: 'single'; key: string; event: ApprovalTimelineEvent }
  | {
      kind: 'group';
      key: string;
      action: string;
      actionLabel: string;
      actorUserId: string;
      actorName: string;
      batchNumber: number | null;
      createdAtUtc: string;
      previousStatusCode: string | null;
      newStatusCode: string | null;
      isDecision: false;
      events: ApprovalTimelineEvent[]; // original events, order preserved
    };

// Timestamp bucket: the same minute. Robust to sub-minute jitter within one submission burst, but a
// real time gap (different minute) — like a later re-submission — still breaks the group.
export function minuteBucket(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return `${d.getUTCFullYear()}-${d.getUTCMonth()}-${d.getUTCDate()}T${d.getUTCHours()}:${d.getUTCMinutes()}`;
}

function canExtend(first: ApprovalTimelineEvent, e: ApprovalTimelineEvent): boolean {
  return e.actionTaken === GROUPABLE_ACTION
    && first.actorUserId === e.actorUserId
    && (first.batchNumber ?? null) === (e.batchNumber ?? null)
    && minuteBucket(first.createdAtUtc) === minuteBucket(e.createdAtUtc);
}

/**
 * Collapse consecutive equivalent BATCH_CANDIDATES_SUBMITTED events into group nodes. A run of a
 * SINGLE such event stays a normal single node (nothing to collapse). Order is preserved exactly.
 */
export function groupTimelineEvents(events: ApprovalTimelineEvent[]): TimelineNode[] {
  const nodes: TimelineNode[] = [];
  let i = 0;
  while (i < events.length) {
    const e = events[i];
    if (e.actionTaken === GROUPABLE_ACTION) {
      // Gather the maximal consecutive equivalent run.
      let j = i + 1;
      while (j < events.length && canExtend(e, events[j])) j++;
      const run = events.slice(i, j);
      if (run.length >= 2) {
        nodes.push({
          kind: 'group',
          key: `grp:${e.id}`,
          action: e.actionTaken,
          actionLabel: e.actionLabel,
          actorUserId: e.actorUserId,
          actorName: e.actorName,
          batchNumber: e.batchNumber ?? null,
          createdAtUtc: e.createdAtUtc,
          previousStatusCode: e.previousStatusCode,
          newStatusCode: e.newStatusCode,
          isDecision: false,
          events: run,
        });
        i = j;
        continue;
      }
    }
    nodes.push({ kind: 'single', key: e.id, event: e });
    i++;
  }
  return nodes;
}

// Deterministic PT presentation for KNOWN, system-generated English context comments only. Arbitrary
// free-text user comments are returned untouched. The raw original is always preserved for display.
const KNOWN_COMMENTS: Record<string, string> = {
  'Request created as Draft.': 'Pedido criado como rascunho.',
  'Request created and submitted for quotation.': 'Pedido criado e submetido para cotação.',
  'Request created and submitted for approval.': 'Pedido criado e submetido para aprovação.',
  'Request submitted for approval.': 'Pedido submetido para aprovação.',
  'Request submitted for quotation.': 'Pedido submetido para cotação.',
};

export interface CommentPresentation {
  text: string;      // what to show prominently (PT when known, else the raw comment)
  raw: string;       // the original stored comment, never altered
  localized: boolean; // true only when a KNOWN system message was mapped
}

export function localizeComment(comment: string | null | undefined): CommentPresentation | null {
  if (comment == null) return null;
  const raw = comment;
  const key = comment.trim();
  const mapped = KNOWN_COMMENTS[key];
  return { text: mapped ?? raw, raw, localized: mapped != null };
}
