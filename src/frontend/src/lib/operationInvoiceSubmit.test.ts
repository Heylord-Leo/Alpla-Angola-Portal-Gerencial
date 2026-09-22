import { describe, it, expect } from 'vitest';
import {
  registerOperationInvoice, createInvoiceSubmitter, releasePreviousUpload, pickResumableAttachment,
  sortRecoverableCandidates,
  type RegisterInvoicePorts,
} from './operationInvoiceSubmit';

// v2.245.8 — upload-order safety of the Final Invoice registration, at the exact boundary the register
// modal calls. Ports are counted; the orchestration is the unit under test.

function ports(over: Partial<RegisterInvoicePorts> = {}) {
  const calls = { preflight: 0, upload: 0, create: 0 };
  const created: string[] = [];
  const p: RegisterInvoicePorts = {
    preflight: async () => { calls.preflight++; },
    upload: async () => { calls.upload++; return 'att-' + calls.upload; },
    create: async (id) => { calls.create++; created.push(id); },
    ...over,
  };
  return { p, calls, created };
}

describe('registerOperationInvoice', () => {
  it('preflight rejection → ZERO uploads, ZERO creates, the preflight error is returned', async () => {
    const boom = new Error('OPERATION_INVOICE_NO_OBLIGATION');
    const { p, calls } = ports({ preflight: async () => { throw boom; } });
    const outcome = await registerOperationInvoice(p);
    expect(outcome).toEqual({ ok: false, stage: 'preflight', error: boom, attachmentId: null });
    expect(calls).toEqual({ preflight: 0, upload: 0, create: 0 });
  });

  it('admissible submission → exactly one upload and one create, in that order', async () => {
    const order: string[] = [];
    const { p } = ports({
      preflight: async () => { order.push('preflight'); },
      upload: async () => { order.push('upload'); return 'att-1'; },
      create: async (id) => { order.push('create:' + id); },
    });
    const outcome = await registerOperationInvoice(p);
    expect(outcome).toEqual({ ok: true, attachmentId: 'att-1', uploaded: true });
    expect(order).toEqual(['preflight', 'upload', 'create:att-1']);
    expect(order.filter(o => o === 'upload')).toHaveLength(1);
    expect(order.filter(o => o.startsWith('create:'))).toHaveLength(1);
  });

  it('upload failure → no create, nothing retained', async () => {
    const { p, calls } = ports({ upload: async () => { throw new Error('upload'); } });
    const outcome = await registerOperationInvoice(p);
    expect(outcome.ok).toBe(false);
    expect((outcome as any).stage).toBe('upload');
    expect((outcome as any).attachmentId).toBeNull();
    expect(calls.create).toBe(0);
  });

  it('create failure AFTER a successful upload → the attachment id is retained for the retry', async () => {
    const { p } = ports({ create: async () => { throw new Error('duplicate'); } });
    const outcome = await registerOperationInvoice(p);
    expect(outcome.ok).toBe(false);
    expect((outcome as any).stage).toBe('create');
    expect((outcome as any).attachmentId).toBe('att-1');
  });

  it('a retry with a retained attachment → NO second upload and NO preflight, one create with the same id', async () => {
    const { p, calls, created } = ports();
    const outcome = await registerOperationInvoice(p, { retainedAttachmentId: 'att-kept' });
    expect(outcome).toEqual({ ok: true, attachmentId: 'att-kept', uploaded: false });
    expect(calls).toEqual({ preflight: 0, upload: 0, create: 1 });
    expect(created).toEqual(['att-kept']);
  });
});

describe('interrupted-registration recovery (server-owned)', () => {
  const u = (attachmentId: string, uploadedAtUtc: string) => ({ attachmentId, uploadedAtUtc });

  it('restores this session\'s own upload automatically ONLY while the server still lists it as unclaimed', () => {
    expect(pickResumableAttachment([u('a', '2026-09-22T10:00:00Z'), u('b', '2026-09-22T11:00:00Z')], 'a')?.attachmentId).toBe('a');
  });

  it('after a closed modal / reload / another session (no own upload) NOTHING is selected automatically', () => {
    expect(pickResumableAttachment([u('a', '2026-09-22T10:00:00Z'), u('b', '2026-09-22T11:00:00Z')], null)).toBeNull();
    expect(pickResumableAttachment([u('only', '2026-09-22T10:00:00Z')], null)).toBeNull();   // one candidate still needs an explicit choice
  });

  it('server truth wins: a retained id the server no longer lists (claimed/released elsewhere) is never resumed', () => {
    expect(pickResumableAttachment([u('b', '2026-09-22T11:00:00Z')], 'a')).toBeNull();
    expect(pickResumableAttachment([], 'a')).toBeNull();
  });

  it('discovered candidates are listed in a deterministic order: newest first, attachment id as tie-break', () => {
    const sorted = sortRecoverableCandidates([
      u('z', '2026-09-22T10:00:00Z'), u('b', '2026-09-22T11:00:00Z'), u('a', '2026-09-22T11:00:00Z'),
    ]);
    expect(sorted.map(s => s.attachmentId)).toEqual(['a', 'b', 'z']);
    // pure: the input is not mutated
    const input = [u('b', '2026-09-22T11:00:00Z'), u('a', '2026-09-22T10:00:00Z')];
    sortRecoverableCandidates(input);
    expect(input.map(i => i.attachmentId)).toEqual(['b', 'a']);
  });

  it('choosing another file releases the previous upload through the backend and reports the outcome', async () => {
    const released: string[] = [];
    expect(await releasePreviousUpload(async (id) => { released.push(id); }, 'att-1', () => false)).toBe('released');
    expect(released).toEqual(['att-1']);
    // an invoice already claims it → never treated as discardable
    expect(await releasePreviousUpload(async () => { throw new Error('claimed'); }, 'att-1', () => true)).toBe('claimed');
    // any other failure keeps the file recoverable (nothing lost)
    expect(await releasePreviousUpload(async () => { throw new Error('network'); }, 'att-1', () => false)).toBe('failed');
  });
});

describe('createInvoiceSubmitter — exactly once under repeated clicks / races', () => {
  it('a second call while the first is in flight is refused as busy and touches no port', async () => {
    let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    const { p, calls } = ports({ create: async () => { await gate; calls.create++; } });
    const submit = createInvoiceSubmitter();

    const first = submit(p);
    const second = await submit(p);        // resolves immediately
    expect(second).toEqual({ ok: false, stage: 'busy', error: null, attachmentId: null });

    release();
    const outcome = await first;
    expect(outcome.ok).toBe(true);
    expect(calls).toEqual({ preflight: 1, upload: 1, create: 1 });
  });

  it('after the first completes, a new submission is accepted again', async () => {
    const { p, calls } = ports();
    const submit = createInvoiceSubmitter();
    await submit(p);
    await submit(p, { retainedAttachmentId: 'att-1' });
    expect(calls).toEqual({ preflight: 1, upload: 1, create: 2 });
  });
});
