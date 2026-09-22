import { describe, it, expect } from 'vitest';
// v2.245.4 — a group whose request items are NOT linked to it must render a read-only remediation blocker
// (never silently nothing) and must expose no action that could bypass the missing linkage.
// Node-env vitest source guards (no jsdom/RTL).
import op from './ReceivingOperation.tsx?raw';

describe('ReceivingOperation — unlinked-items remediation blocker', () => {
  it('derives the unlinked items generically from the request (null or unknown group id)', () => {
    expect(op).toMatch(/const unlinkedItems = useMemo\(/);
    expect(op).toMatch(/!i\.requestPoGroupId \|\| !groupIds\.has\(i\.requestPoGroupId\)/);
  });

  it('an empty group stays silent only when the request has no unlinked items', () => {
    expect(op).toMatch(/if \(groupItems\.length === 0\) \{/);
    expect(op).toMatch(/if \(unlinkedItems\.length === 0\) return null;/);
  });

  it('renders the blocker for a victim group and explains controlled administrative remediação', () => {
    expect(op).toMatch(/data-testid="linkage-blocker"/);
    expect(op).toMatch(/não vinculado\(s\) ao grupo de recebimento/);
    expect(op).toMatch(/remediação administrativa controlada/);
  });

  it('the blocker offers no item registration or confirmation action', () => {
    const start = op.indexOf('data-testid="linkage-blocker"');
    const end = op.indexOf('const groupActionable = isReceivingActionableGroupStatus(group.status)');
    expect(start).toBeGreaterThan(-1);
    expect(end).toBeGreaterThan(start);
    const blocker = op.slice(start, end);
    expect(blocker).not.toMatch(/REGISTRAR/);
    expect(blocker).not.toMatch(/CONFIRMAR RECEBIMENTO/);
    expect(blocker).not.toMatch(/handleFinalizeClick|handleOpenModal|api\./);
  });

  it('the item table and confirm action still render for properly linked groups', () => {
    expect(op).toMatch(/operationalItems\.filter\(\(i: any\) => i\.requestPoGroupId === group\.id\)/);
    expect(op).toMatch(/CONFIRMAR RECEBIMENTO/);
  });
});
