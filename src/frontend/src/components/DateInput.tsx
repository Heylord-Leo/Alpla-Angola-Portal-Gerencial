import React, { useState, useEffect, useRef } from 'react';
import { Calendar } from 'lucide-react';

interface DateInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'> {
    value?: string; // Expects YYYY-MM-DD
    onChange: (value: string) => void; // Emits YYYY-MM-DD
    hasError?: boolean;
}

/**
 * A reusable Date Input component that enforces DD/MM/YYYY display format
 * while maintaining YYYY-MM-DD backend compatibility.
 * Now includes a hidden native date picker bridge for calendar support.
 */
export function DateInput({ value, onChange, hasError, style, className, ...props }: DateInputProps) {
    const [displayValue, setDisplayValue] = useState<string>('');
    const hiddenDateRef = useRef<HTMLInputElement>(null);

    // Format YYYY-MM-DD to DD/MM/YYYY
    const toDisplay = (val: string | undefined | null) => {
        if (!val || val.length < 10) return '';
        const parts = val.split('-');
        if (parts.length !== 3) return '';
        return `${parts[2]}/${parts[1]}/${parts[0]}`;
    };

    // Format DD/MM/YYYY to YYYY-MM-DD
    const toRaw = (display: string) => {
        if (display.length !== 10) return '';
        const parts = display.split('/');
        if (parts.length !== 3) return '';
        const [d, m, y] = parts;
        
        const day = parseInt(d, 10);
        const month = parseInt(m, 10);
        const year = parseInt(y, 10);
        
        if (isNaN(day) || isNaN(month) || isNaN(year)) return '';
        if (month < 1 || month > 12) return '';
        if (day < 1 || day > 31) return '';
        if (year < 1900 || year > 2100) return '';

        const date = new Date(Date.UTC(year, month - 1, day));
        if (date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return '';

        return `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}`;
    };

    // Sync display when parent value changes
    useEffect(() => {
        const expectedDisplay = toDisplay(value);
        if (toRaw(displayValue) !== value) {
            setDisplayValue(expectedDisplay);
        }
    }, [value]);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        let val = e.target.value;
        val = val.replace(/\D/g, '');

        let formatted = '';
        if (val.length > 0) {
            formatted += val.substring(0, 2);
            if (val.length > 2) {
                formatted += '/' + val.substring(2, 4);
                if (val.length > 4) {
                    formatted += '/' + val.substring(4, 8);
                }
            }
        }

        setDisplayValue(formatted);

        if (formatted.length === 10) {
            const raw = toRaw(formatted);
            onChange(raw || '');
        } else if (formatted.length === 0) {
            onChange('');
        } else if (value !== '') {
            onChange('');
        }
    };

    const handleCalendarClick = () => {
        if (!hiddenDateRef.current) return;
        
        // Use showPicker if available (Chrome 99+, Safari 16+, Firefox 101+)
        if ('showPicker' in HTMLInputElement.prototype) {
            try {
                hiddenDateRef.current.showPicker();
            } catch (error) {
                console.warn('showPicker failed, falling back to click', error);
                hiddenDateRef.current.click();
            }
        } else {
            hiddenDateRef.current.click();
        }
    };

    const handleHiddenDateChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const val = e.target.value; // YYYY-MM-DD from native input
        if (val) {
            onChange(val);
        }
    };

    const containerStyle: React.CSSProperties = {
        position: 'relative',
        display: 'flex',
        alignItems: 'center',
        width: '100%'
    };

    const combinedStyle: React.CSSProperties = {
        textAlign: 'left',
        paddingRight: '40px', // Space for the icon
        ...style,
        ...(hasError ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2' } : {})
    };

    const pickerButtonStyle: React.CSSProperties = {
        position: 'absolute',
        right: '12px',
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '4px',
        color: 'var(--color-text-muted)',
        transition: 'color 0.2s ease',
        zIndex: 2
    };

    return (
        <div style={containerStyle} className={className}>
            <input
                {...props}
                type="text"
                placeholder="DD/MM/YYYY"
                value={displayValue}
                onChange={handleChange}
                style={combinedStyle}
                maxLength={10}
            />
            
            <button
                type="button"
                onClick={handleCalendarClick}
                style={pickerButtonStyle}
                aria-label="Abrir calendário"
                title="Abrir calendário"
            >
                <Calendar size={18} />
            </button>

            {/* Hidden native picker bridge */}
            <input
                ref={hiddenDateRef}
                type="date"
                value={value || ''}
                onChange={handleHiddenDateChange}
                aria-hidden="true"
                tabIndex={-1}
                style={{
                    position: 'absolute',
                    top: 0,
                    left: 0,
                    width: '100%',
                    height: '100%',
                    opacity: 0,
                    pointerEvents: 'none', // Prevents it from stealing focus or clicks
                    zIndex: -1
                }}
            />
        </div>
    );
}
