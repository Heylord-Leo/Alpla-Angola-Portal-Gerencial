import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guard (no jsdom/RTL).
import src from './LoginPage.tsx?raw';

// v2.238.0 TEST-acceptance defect: in dark mode the login inputs kept a hard-coded light background
// with no explicit text color, so the inherited near-white --color-text-main rendered invisible text.
// Guard: the input style must pair a token background with a token text color, and must not resurrect
// the defective hard-coded light background.
describe('LoginPage — dark-mode input contrast', () => {
  it('input uses a theme text color (not inherited near-white on a light field)', () => {
    const inputStyle = src.split('input: {')[1]?.split('},')[0] ?? '';
    expect(inputStyle).toMatch(/color:\s*'var\(--color-text-main\)'/);
    expect(inputStyle).toMatch(/backgroundColor:\s*'var\(--color-bg-surface\)'/);
  });

  it('does not reintroduce the hard-coded light input background (#fcfcfc)', () => {
    expect(src).not.toMatch(/#fcfcfc/i);
  });

  it('uses only defined theme tokens (no undefined --color-text / --color-bg-elevated)', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
  });

  it('does not touch authentication behavior (guard is styling-only)', () => {
    // The fix must not alter the login/auth flow — the field text/background pairing is the only change.
    expect(src).toMatch(/type="email"/);
    expect(src).toMatch(/type=\{showPassword \? "text" : "password"\}/);
  });
});
