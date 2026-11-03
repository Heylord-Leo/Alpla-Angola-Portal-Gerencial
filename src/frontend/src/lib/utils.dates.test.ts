import { describe, it, expect, beforeAll } from 'vitest';
import { formatDate, formatDateTime, formatTime, formatCalendarDate, parseBackendInstant, getUrgencyStyle } from './utils';

// v2.249.4 — Print View showed quotation / required dates one day EARLIER on a UTC+1 browser (TEST, REQ-628: PDF 29/10/2026 →
// report 28/10/2026; required date 04/11/2026 → 03/11/2026). The API serializes date-only business fields and every *Utc
// column WITHOUT an offset ("2026-11-04T00:00:00", DateTimeKind.Unspecified); `new Date(offsetless)` is browser-local and the
// UTC getters then returned the previous day. The helpers now read an offsetless string as UTC. These tests run under the
// Angola offset so the regression is reproducible on any CI machine.

// Node only (vitest environment: node). The process object is reached via globalThis so the browser tsconfig (no @types/node) stays clean.
beforeAll(() => { (globalThis as any).process.env.TZ = 'Africa/Luanda'; });

describe('parseBackendInstant — offsetless backend strings are UTC', () => {
    it('the test process really runs at UTC+1 (otherwise the regression would be invisible)', () => {
        expect(new Date('2026-11-04T00:00:00').getTimezoneOffset()).toBe(-60);
    });
    it('offsetless datetime → UTC midnight of the same calendar day', () => {
        expect(parseBackendInstant('2026-11-04T00:00:00').toISOString()).toBe('2026-11-04T00:00:00.000Z');
        expect(parseBackendInstant('2026-10-29T00:00:00').getUTCDate()).toBe(29);
    });
    it('seven-digit SQL Server fraction, Z suffix, +00:00 and bare YYYY-MM-DD are all read as the same instant', () => {
        for (const v of ['2026-10-29T00:00:00.0000000', '2026-10-29T00:00:00Z', '2026-10-29T00:00:00+00:00', '2026-10-29']) {
            expect(parseBackendInstant(v).toISOString(), v).toBe('2026-10-29T00:00:00.000Z');
        }
    });
    it('a string with a non-zero offset keeps its own offset', () => {
        expect(parseBackendInstant('2026-10-29T01:00:00+01:00').toISOString()).toBe('2026-10-29T00:00:00.000Z');
    });
    it('a Date instance is returned unchanged', () => {
        const d = new Date(Date.UTC(2026, 9, 29));
        expect(parseBackendInstant(d)).toBe(d);
    });
});

describe('formatDate / formatDateTime — the day never shifts', () => {
    it('REQ-628 shapes: quotation date and required date as the API really sends them', () => {
        expect(formatDate('2026-10-29T00:00:00')).toBe('29/10/2026');   // MAQSIS quotation (PDF 29/10/2026)
        expect(formatDate('2026-10-28T00:00:00')).toBe('28/10/2026');   // TREVOTECH / MULTIBIZ quotations
        expect(formatDate('2026-11-04T00:00:00')).toBe('04/11/2026');   // required date shown 04/11 in the UI
    });
    it('boundaries: first/last day of month and year, leap day, DST-free Angola', () => {
        expect(formatDate('2026-01-01T00:00:00')).toBe('01/01/2026');
        expect(formatDate('2026-12-31T00:00:00')).toBe('31/12/2026');
        expect(formatDate('2028-02-29T00:00:00')).toBe('29/02/2028');
        expect(formatDate('2026-03-01T00:00:00')).toBe('01/03/2026');
    });
    it('instants with a suffix are unchanged (UTC calendar day, as before)', () => {
        expect(formatDate('2026-09-20T00:00:00Z')).toBe('20/09/2026');
        expect(formatDate('2026-09-20T23:59:59Z')).toBe('20/09/2026');
        expect(formatDateTime('2026-10-29T10:15:00Z')).toBe('29/10/2026 10:15');
    });
    it('an offsetless audit timestamp is a UTC instant (the backend stores UTC): no local re-interpretation', () => {
        expect(formatDateTime('2026-10-29T23:30:00')).toBe('29/10/2026 23:30');
        expect(formatTime('2026-10-29T23:30:00')).toBe('23:30');
    });
    it('empty / invalid → "-"', () => {
        expect(formatDate(null)).toBe('-');
        expect(formatDate(undefined)).toBe('-');
        expect(formatDate('')).toBe('-');
        expect(formatDate('not-a-date')).toBe('-');
    });
});

describe('getUrgencyStyle — deadline compared on the same calendar', () => {
    it('an offsetless need-by equal to today (UTC) is "vence hoje", not "vencido"', () => {
        const now = new Date();
        const today = `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}-${String(now.getUTCDate()).padStart(2, '0')}T00:00:00`;
        expect(getUrgencyStyle(today, 'WAITING_QUOTATION')?.description).toBe('ESTE PEDIDO VENCE HOJE');
    });
});

// ── Review point 2: the four value shapes the API really produces, through each shared formatter (browser UTC+1) ──
// Representative raw values: date-only field as serialized by System.Text.Json from datetime2 (offsetless midnight), an
// offsetless audit timestamp (DateTimeKind.Unspecified, stored UTC), a Z timestamp (properties with UtcDateTimeJsonConverter)
// and an explicit offset (never produced by this API; shown for completeness).
describe('shape × formatter matrix (v2.249.4)', () => {
    const rows: Array<[string, string, string, string, string, string]> = [
        // raw value                         formatCalendarDate  formatDate     formatDateTime        formatTime  note
        ['2026-11-04',                        '04/11/2026', '04/11/2026', '04/11/2026 00:00', '00:00', 'bare date-only (inputs)'],
        ['2026-11-04T00:00:00',               '04/11/2026', '04/11/2026', '04/11/2026 00:00', '00:00', 'date-only field from the API (NeedByDateUtc, DocumentDate)'],
        ['2026-10-29T00:00:00.0000000',       '29/10/2026', '29/10/2026', '29/10/2026 00:00', '00:00', 'date-only with SQL Server fraction'],
        ['2026-10-29T14:30:00',               '29/10/2026', '29/10/2026', '29/10/2026 14:30', '14:30', 'offsetless audit timestamp (UploadedAtUtc, CancelledAtUtc): UTC by contract'],
        ['2026-10-29T23:30:00',               '29/10/2026', '29/10/2026', '29/10/2026 23:30', '23:30', 'offsetless timestamp near midnight: previously 28/10 23:30 on UTC+1 (wrong)'],
        ['2026-10-29T23:30:00Z',              '29/10/2026', '29/10/2026', '29/10/2026 23:30', '23:30', 'Z timestamp (UtcDateTimeJsonConverter): unchanged'],
        ['2026-10-30T00:30:00+01:00',         '30/10/2026', '29/10/2026', '29/10/2026 23:30', '23:30', 'explicit offset: instants convert to UTC; the calendar formatter keeps the written date'],
    ];
    it.each(rows)('%s', (raw, calendar, date, dateTime, time) => {
        expect(formatCalendarDate(raw)).toBe(calendar);
        expect(formatDate(raw)).toBe(date);
        expect(formatDateTime(raw)).toBe(dateTime);
        expect(formatTime(raw)).toBe(time);
    });
    it('formatCalendarDate never converts: no Date object for strings; a Date instance falls back to the UTC calendar day', () => {
        expect(formatCalendarDate('2026-11-04T00:00:00')).toBe('04/11/2026');
        expect(formatCalendarDate(new Date(Date.UTC(2026, 10, 4)))).toBe('04/11/2026');
        expect(formatCalendarDate('')).toBe('-'); expect(formatCalendarDate(null)).toBe('-'); expect(formatCalendarDate('04/11/2026')).toBe('-');
    });
    it('the old behaviour is gone: an offsetless timestamp is no longer shifted by the browser offset', () => {
        // What formatDateTime used to show for "2026-10-29T14:30:00" on UTC+1: local parse (14:30+01:00 = 13:30Z) + UTC getters → 13:30.
        const legacy = new Date('2026-10-29T14:30:00');
        expect(`${String(legacy.getUTCHours()).padStart(2, '0')}:${String(legacy.getUTCMinutes()).padStart(2, '0')}`).toBe('13:30');
        expect(formatDateTime('2026-10-29T14:30:00')).toBe('29/10/2026 14:30');
    });
});
