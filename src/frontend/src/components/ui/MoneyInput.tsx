import React, { useEffect, useState } from 'react';
import {
    formatMoneyInput,
    moneyEditingValue,
    needsPasteNormalization,
    parseMoneyInput,
    sanitizeMoneyTyping
} from '../../lib/money';

export interface MoneyInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
    /** Canonical decimal value ("123456.78"), a number, or ''/null for blank. */
    value?: string | number | null;
    /** Receives the canonical decimal string ("123456.78") or '' while the field is blank. */
    onChange: (canonical: string) => void;
    hasError?: boolean;
    allowNegative?: boolean;
}

/**
 * The Portal's monetary input (v2.229.8) — locale-independent by construction.
 *
 * A controlled `type="text"` field (never `type="number"`: no browser spinners, no
 * locale-dependent decimal separator, no silent refusal of "," on an English Windows):
 *  · while focused, the user types digits and either "." or "," as the decimal separator
 *    (max 2 decimals); pasted text ("120 000,00", "120,000.00", "120.000,00", "120000.5")
 *    is normalized through the documented policy in `lib/money.ts`;
 *  · while blurred, the value presents in the Portal's pt-AO convention: "120 000,00";
 *  · the parent always receives the canonical decimal string ("120000.00"), so what the
 *    API receives is numerically identical to today's contracts.
 */
export function MoneyInput({
    value, onChange, hasError, allowNegative = false, style, onFocus, onBlur, ...props
}: MoneyInputProps) {
    const [focused, setFocused] = useState(false);
    const [text, setText] = useState('');

    // While NOT focused the display always mirrors the parent value; while focused the
    // user's editing text is authoritative (a programmatic mid-edit overwrite would fight
    // the cursor).
    useEffect(() => {
        if (!focused) setText(formatMoneyInput(value));
    }, [value, focused]);

    const handleFocus = (e: React.FocusEvent<HTMLInputElement>) => {
        setFocused(true);
        setText(moneyEditingValue(value));
        onFocus?.(e);
    };

    const handleBlur = (e: React.FocusEvent<HTMLInputElement>) => {
        setFocused(false);
        const canonical = parseMoneyInput(text);
        setText(canonical === null ? '' : formatMoneyInput(canonical));
        onChange(canonical ?? '');
        onBlur?.(e);
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const next = e.target.value;
        // Paste with grouping/mixed separators → full normalization to the editing form;
        // plain typing → light filter that keeps the cursor stable.
        const cleaned = needsPasteNormalization(next)
            ? moneyEditingValue(parseMoneyInput(next))
            : sanitizeMoneyTyping(next, allowNegative);
        setText(cleaned);
        onChange(parseMoneyInput(cleaned) ?? '');
    };

    return (
        <input
            {...props}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={text}
            onChange={handleChange}
            onFocus={handleFocus}
            onBlur={handleBlur}
            style={{
                textAlign: 'right' as const,
                ...style,
                ...(hasError ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2' } : {})
            }}
        />
    );
}
