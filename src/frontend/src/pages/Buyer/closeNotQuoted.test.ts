import { describe, it, expect } from 'vitest';
import { isCloseNotQuotedValid, isLastPendingItem, MIN_CLOSE_JUSTIFICATION_LENGTH } from './closeNotQuoted';

// Phase 3E.1 — the close-not-quoted form contract shared by classic + Workspace. Eligibility of items is
// server-computed (item.canCloseNotQuoted) and not re-derived here; these pin the modal's form validation.

describe('isCloseNotQuotedValid', () => {
  const long = 'x'.repeat(MIN_CLOSE_JUSTIFICATION_LENGTH);

  it('requires a reason', () => {
    expect(isCloseNotQuotedValid('', long)).toBe(false);
  });
  it('requires justification at least the minimum length', () => {
    expect(isCloseNotQuotedValid('Item não é mais necessário', 'muito curto')).toBe(false);
    expect(isCloseNotQuotedValid('Item não é mais necessário', long)).toBe(true);
  });
  it('trims whitespace before measuring the justification', () => {
    expect(isCloseNotQuotedValid('Outro', '   ' + 'y'.repeat(MIN_CLOSE_JUSTIFICATION_LENGTH - 1) + '   ')).toBe(false);
    expect(isCloseNotQuotedValid('Outro', '  ' + long + '  ')).toBe(true);
  });
  it('rejects an empty submission', () => {
    expect(isCloseNotQuotedValid('', '')).toBe(false);
  });
});

describe('isLastPendingItem', () => {
  it('is true only when exactly one pending item remains', () => {
    expect(isLastPendingItem(1)).toBe(true);
    expect(isLastPendingItem(0)).toBe(false);
    expect(isLastPendingItem(2)).toBe(false);
  });
});
