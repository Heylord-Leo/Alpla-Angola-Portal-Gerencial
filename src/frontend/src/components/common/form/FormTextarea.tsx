
import { FormFieldWrapper, FormFieldWrapperProps, inputStyle } from './FormFieldWrapper';

export interface FormTextareaProps extends Omit<FormFieldWrapperProps, 'children'> {
    value: string;
    onChange: (value: string) => void;
    rows?: number;
    placeholder?: string;
    disabled?: boolean;
    name?: string;
    id?: string;
    maxLength?: number;
}

export function FormTextarea({
    label,
    required,
    error,
    helperText,
    className,
    style,
    value,
    onChange,
    rows = 3,
    placeholder,
    disabled,
    name,
    id,
    maxLength
}: FormTextareaProps) {
    return (
        <FormFieldWrapper
            label={label}
            required={required}
            error={error}
            helperText={helperText}
            className={className}
            style={style}
        >
            <textarea
                id={id}
                name={name}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                disabled={disabled}
                placeholder={placeholder}
                rows={rows}
                maxLength={maxLength}
                style={{
                    ...inputStyle,
                    resize: 'vertical',
                    opacity: disabled ? 0.6 : 1,
                    borderColor: error ? '#ef4444' : 'var(--color-border)'
                }}
            />
        </FormFieldWrapper>
    );
}
