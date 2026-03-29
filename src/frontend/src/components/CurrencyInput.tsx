import React, { useState, useEffect } from 'react';

interface CurrencyInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'> {
    value?: string | number | null;
    onChange: (value: string) => void;
    hasError?: boolean;
}

export function CurrencyInput({ value, onChange, hasError, style, ...props }: CurrencyInputProps) {
    const [displayValue, setDisplayValue] = useState<string>('');

    // Format raw float string ("10.50") to display string ("10,50")
    const formatValue = (val: string | number | null | undefined) => {
        if (val === null || val === undefined || val === '') return '';
        const num = parseFloat(String(val));
        if (isNaN(num)) return '';

        // Use Intl.NumberFormat for Angolan Kwanza / European formatting
        return new Intl.NumberFormat('pt-AO', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }).format(num);
    };

    // When parent value changes programmatically, sync our display
    useEffect(() => {
        // Prevent update cycle if we are actively typing and the floats match
        const currentFloat = parseFloat(displayValue.replace(/\./g, '').replace(',', '.') || '0');
        const incomingFloat = parseFloat(String(value || '0'));

        if (currentFloat !== incomingFloat) {
            setDisplayValue(formatValue(value));
        }
    }, [value]);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        let val = e.target.value;

        // Strip out anything that isn't a digit
        val = val.replace(/\D/g, '');

        if (!val) {
            setDisplayValue('');
            onChange('');
            return;
        }

        // Treat the input string of digits as cents
        // e.g. "123" -> 1.23
        const floatValue = parseInt(val, 10) / 100;

        setDisplayValue(formatValue(floatValue));

        // Parent stores standard float format for the API: "1.23"
        onChange(floatValue.toFixed(2));
    };

    const combinedStyle = {
        textAlign: 'right' as const,
        ...style,
        ...(hasError ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2' } : {})
    };

    return (
        <input
            {...props}
            type="text"
            inputMode="numeric"
            value={displayValue}
            onChange={handleChange}
            style={combinedStyle}
        />
    );
}
