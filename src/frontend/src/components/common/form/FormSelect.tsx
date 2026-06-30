
import { FormFieldWrapper, FormFieldWrapperProps, inputStyle } from './FormFieldWrapper';

export interface FormSelectOption {
    value: string | number;
    label: string;
}

export interface FormSelectProps extends Omit<FormFieldWrapperProps, 'children'> {
    value: string | number;
    onChange: (value: string) => void;
    options: FormSelectOption[];
    disabled?: boolean;
    name?: string;
    id?: string;
    placeholder?: string;
}

export function FormSelect({
    label,
    required,
    error,
    helperText,
    className,
    style,
    value,
    onChange,
    options,
    disabled,
    name,
    id,
    placeholder
}: FormSelectProps) {
    return (
        <FormFieldWrapper
            label={label}
            required={required}
            error={error}
            helperText={helperText}
            className={className}
            style={style}
        >
            <select
                id={id}
                name={name}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                disabled={disabled}
                style={{
                    ...inputStyle,
                    opacity: disabled ? 0.6 : 1,
                    borderColor: error ? '#ef4444' : 'var(--color-border)'
                }}
            >
                {placeholder && (
                    <option value="" disabled>
                        {placeholder}
                    </option>
                )}
                {options.map((opt) => (
                    <option key={String(opt.value)} value={opt.value}>
                        {opt.label}
                    </option>
                ))}
            </select>
        </FormFieldWrapper>
    );
}
