import { describe, it, expect } from 'vitest';
import { parseFinanceReturnComment } from './financeReturnReason';

describe('parseFinanceReturnComment — v2.242.0', () => {
  it('extracts the human reason and separates the technical context (canonical format)', () => {
    const raw = '[Lote #1 | KRONES ANGOLA | Moeda: AOA | Total: 4.616.502,39] Grupo devolvido por Finanças para correção da P.O (GroupId: 02a5d2b4-bc1e-4d87-9bd1-75caea1f8830). Motivo: P.O INCORRETA';
    const p = parseFinanceReturnComment(raw);
    expect(p.message).toBe('P.O INCORRETA');           // prominent human message only
    expect(p.hadReason).toBe(true);
    expect(p.technical).toContain('Lote #1');           // bracket + system sentence kept as technical
    expect(p.technical).toContain('GroupId');
    expect(p.message).not.toContain('GroupId');         // technical never leaks into the message
    expect(p.message).not.toContain('Lote #1');
  });

  it('handles the legacy "Devolvido por Finanças para ajuste:" prefix', () => {
    const p = parseFinanceReturnComment('Devolvido por Finanças para ajuste: valor errado');
    expect(p.message).toBe('valor errado');
    expect(p.technical).toBeNull();
    expect(p.hadReason).toBe(true);
  });

  it('falls back to a neutral message when the reason is empty (Motivo: <blank>)', () => {
    const p = parseFinanceReturnComment('[Lote #1 | X | Moeda: AOA | Total: 1,00] Grupo devolvido ... Motivo: ');
    expect(p.message).toBe('Finanças devolveu esta P.O. para correção.');
    expect(p.hadReason).toBe(false);
    expect(p.technical).toContain('Lote #1'); // technical preserved even without a reason
  });

  it('uses a neutral message for an empty/absent comment', () => {
    expect(parseFinanceReturnComment('').message).toBe('Finanças devolveu esta P.O. para correção.');
    expect(parseFinanceReturnComment(null).hadReason).toBe(false);
    expect(parseFinanceReturnComment(undefined).message).toBe('Finanças devolveu esta P.O. para correção.');
  });

  it('peels a leading technical bracket when there is no "Motivo:" and keeps the rest as the message', () => {
    const p = parseFinanceReturnComment('[Lote #2 | Y | Moeda: AOA | Total: 2,00] documento ilegível');
    expect(p.message).toBe('documento ilegível');
    expect(p.technical).toBe('[Lote #2 | Y | Moeda: AOA | Total: 2,00]');
  });

  it('never discards an unknown-shape comment — shows it whole as the message', () => {
    const p = parseFinanceReturnComment('mensagem antiga sem formato');
    expect(p.message).toBe('mensagem antiga sem formato');
    expect(p.technical).toBeNull();
    expect(p.hadReason).toBe(true);
  });
});
