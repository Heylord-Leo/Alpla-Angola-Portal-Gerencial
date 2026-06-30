
import { FormFieldWrapper, FormFieldWrapperProps } from './FormFieldWrapper';

export interface FormCheckboxProps extends Omit<FormFieldWrapperProps, 'children'> {
    checked: boolean;
    onChange: (checked: boolean) => void;
    disabled?: boolean;
    name?: string;
    id?: string;
}

export function FormCheckbox({
    label,
    required,
    error,
    helperText,
    className,
    style,
    checked,
    onChange,
    disabled,
    name,
    id
}: FormCheckboxProps) {
    return (
        <FormFieldWrapper
            error={error}
            helperText={helperText}
            className={className}
            style={style}
        >
            <label style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                cursor: disabled ? 'not-allowed' : 'pointer',
                opacity: disabled ? 0.6 : 1,
                userSelect: 'none'
            }}>
                <input
                    id={id}
                    name={name}
                    type="checkbox"
                    checked={checked}
                    onChange={(e) => onChange(e.target.checked)}
                    disabled={disabled}
                    style={{
                        width: '16px',
                        height: '16px',
                        margin: 0,
                        cursor: disabled ? 'not-allowed' : 'pointer'
                    }}
                />
                <span style={{
                    fontSize: '0.85rem',
                    color: 'var(--color-text)',
                    fontWeight: 500
                }}>
                    {label}
                    {required && <span style={{ color: '#ef4444', marginLeft: 2 }}>*</span>}
                </span>
            </label>
        </FormFieldWrapper>
    );
}
