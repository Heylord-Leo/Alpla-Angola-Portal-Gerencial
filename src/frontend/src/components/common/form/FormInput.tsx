
import { FormFieldWrapper, FormFieldWrapperProps, inputStyle } from './FormFieldWrapper';

export interface FormInputProps extends Omit<FormFieldWrapperProps, 'children'> {
    value: string | number;
    onChange: (value: string) => void;
    type?: 'text' | 'password' | 'email' | 'number' | 'tel' | 'url' | 'date';
    placeholder?: string;
    disabled?: boolean;
    name?: string;
    id?: string;
    autoComplete?: string;
    maxLength?: number;
    onKeyDown?: (e: React.KeyboardEvent<HTMLInputElement>) => void;
}

export function FormInput({
    label,
    required,
    error,
    helperText,
    className,
    style,
    value,
    onChange,
    type = 'text',
    placeholder,
    disabled,
    name,
    id,
    autoComplete,
    maxLength,
    onKeyDown
}: FormInputProps) {
    return (
        <FormFieldWrapper
            label={label}
            required={required}
            error={error}
            helperText={helperText}
            className={className}
            style={style}
        >
            <input
                id={id}
                name={name}
                type={type}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                disabled={disabled}
                placeholder={placeholder}
                autoComplete={autoComplete}
                maxLength={maxLength}
                onKeyDown={onKeyDown}
                style={{
                    ...inputStyle,
                    opacity: disabled ? 0.6 : 1,
                    borderColor: error ? '#ef4444' : 'var(--color-border)'
                }}
            />
        </FormFieldWrapper>
    );
}
