import { MoneyInput, MoneyInputProps } from './ui/MoneyInput';

export type CurrencyInputProps = MoneyInputProps;

/**
 * Backwards-compatible alias of the shared {@link MoneyInput} (v2.229.8).
 *
 * The previous implementation typed "ATM-style" (every keystroke shifted cents: typing
 * 120000 produced 1 200,00) and silently discarded "." and "," — confirmed as a live UX
 * defect on REQ-18/08/2026-233, where monetary entry depended on the Windows/browser
 * locale. All consumers keep the exact same props contract (canonical decimal string out,
 * pt-AO display) and inherit the corrected free-typing model: digits plus either "." or ","
 * as the decimal separator, thousands grouping on blur, 2 decimal places.
 */
export function CurrencyInput(props: CurrencyInputProps) {
    return <MoneyInput {...props} />;
}
