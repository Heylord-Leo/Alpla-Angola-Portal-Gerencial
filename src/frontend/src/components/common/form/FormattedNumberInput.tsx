import React, { useState, useEffect } from 'react';
import { formatCurrencyAO } from '../../../lib/utils';

interface FormattedNumberInputProps {
    value: number | null | undefined;
    onChange: (value: number | null) => void;
    currencyCode?: string;
    style?: React.CSSProperties;
    placeholder?: string;
    disabled?: boolean;
}

export function FormattedNumberInput({
    value,
    onChange,
    currencyCode,
    style,
    placeholder,
    disabled
}: FormattedNumberInputProps) {
    const [isFocused, setIsFocused] = useState(false);
    const [localValue, setLocalValue] = useState('');

    useEffect(() => {
        if (!isFocused) {
            setLocalValue(value == null ? '' : value.toString());
        }
    }, [value, isFocused]);

    const handleFocus = () => {
        setIsFocused(true);
        setLocalValue(value == null ? '' : value.toString());
    };

    const handleBlur = () => {
        setIsFocused(false);
        if (localValue.trim() === '') {
            onChange(null);
        } else {
            // Try to parse the number, handling commas as decimals if typed manually
            let normalizedStr = localValue.replace(/ /g, '');
            // If they typed a comma instead of a dot for decimal separator
            if (normalizedStr.includes(',') && !normalizedStr.includes('.')) {
                normalizedStr = normalizedStr.replace(',', '.');
            }
            const parsed = parseFloat(normalizedStr);
            if (!isNaN(parsed)) {
                onChange(parsed);
                setLocalValue(parsed.toString());
            } else {
                // Invalid input, revert to previous value
                setLocalValue(value == null ? '' : value.toString());
            }
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setLocalValue(e.target.value);
    };

    const displayValue = isFocused
        ? localValue
        : (value == null ? '' : formatCurrencyAO(value, currencyCode));

    return (
        <input
            type="text"
            value={displayValue}
            onFocus={handleFocus}
            onBlur={handleBlur}
            onChange={handleChange}
            style={style}
            placeholder={placeholder}
            disabled={disabled}
        />
    );
}
