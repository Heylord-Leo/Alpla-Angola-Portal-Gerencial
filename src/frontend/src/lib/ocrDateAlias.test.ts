import { describe, it, expect } from 'vitest';
import { resolveOcrDateAlias, describeQuotationDueDateSource } from './ocrDateAlias';

// v2.249.8 — finding 3 of the v2.249.7 TEST validation: the quotation validity date shown by the wizard differed between
// imports of the same PDF (30/11/2026 printed; 01/12/2026 once shown). The deterministic part of the pipeline is pinned here:
// the alias never defaults, never converts a timezone and never shifts a calendar day.
describe('resolveOcrDateAlias', () => {
    it('keeps YYYY-MM-DD unchanged and never shifts the day (30/11 printed stays 2026-11-30)', () => {
        expect(resolveOcrDateAlias('2026-11-30')).toBe('2026-11-30');
        expect(resolveOcrDateAlias(' 2026-11-30 ')).toBe('2026-11-30');
    });
    it('day-first printed forms (dd/MM/yyyy, dd-MM-yyyy, dd.MM.yyyy) become the same calendar day', () => {
        expect(resolveOcrDateAlias('30/11/2026')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('30-11-2026')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('30.11.2026')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('1/12/2026')).toBe('2026-12-01');
    });
    it('year-first with slashes / dots is normalised too', () => {
        expect(resolveOcrDateAlias('2026/11/30')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('2026.11.30')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('2026/1/5')).toBe('2026-01-05');
    });
    it('an ISO date-time keeps its calendar day as written — no Date round-trip, so a UTC offset cannot move 30/11 to 01/12', () => {
        expect(resolveOcrDateAlias('2026-11-30T00:00:00')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('2026-11-30T23:30:00Z')).toBe('2026-11-30');
        expect(resolveOcrDateAlias('2026-11-30 00:00')).toBe('2026-11-30');
    });
    it('no value → empty (no default, no "today"); an unrecognised value is returned trimmed, never replaced by another date', () => {
        expect(resolveOcrDateAlias(undefined)).toBe('');
        expect(resolveOcrDateAlias(null)).toBe('');
        expect(resolveOcrDateAlias('')).toBe('');
        expect(resolveOcrDateAlias('30 dias')).toBe('30 dias');
        expect(resolveOcrDateAlias('validade: 30 dias')).toBe('validade: 30 dias');
    });
    it('is a pure function of its input (no clock)', () => {
        const a = resolveOcrDateAlias('30/11/2026');
        const b = resolveOcrDateAlias('30/11/2026');
        expect(a).toBe(b);
        expect(a).not.toBe(new Date().toISOString().slice(0, 10));
    });
});

describe('describeQuotationDueDateSource', () => {
    it('names the provenance in pt-PT', () => {
        expect(describeQuotationDueDateSource('EXTRACTED')).toBe('lida do documento pela extração');
        expect(describeQuotationDueDateSource('MANUAL')).toBe('introduzida manualmente');
        expect(describeQuotationDueDateSource(undefined)).toBe('origem não registada');
    });
});
