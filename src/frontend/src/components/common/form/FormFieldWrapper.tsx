import React from 'react';

export interface FormFieldWrapperProps {
    label?: string;
    required?: boolean;
    error?: string;
    helperText?: string;
    children: React.ReactNode;
    className?: string;
    style?: React.CSSProperties;
}

export const labelStyle: React.CSSProperties = {
    display: 'block', 
    fontSize: '0.82rem', 
    fontWeight: 600, 
    color: 'var(--color-text)',
    marginBottom: 4
};

export const inputStyle: React.CSSProperties = {
    width: '100%', 
    padding: '10px 14px', 
    border: '1px solid var(--color-border)',
    borderRadius: 6, 
    backgroundColor: 'var(--color-bg-surface)', 
    color: 'var(--color-text)',
    fontSize: '0.85rem', 
    outline: 'none', 
    boxSizing: 'border-box'
};

export function FormFieldWrapper({
    label,
    required,
    error,
    helperText,
    children,
    className,
    style
}: FormFieldWrapperProps) {
    return (
        <div className={className} style={{ flex: 1, ...style }}>
            {label && (
                <label style={labelStyle}>
                    {label}
                    {required && <span style={{ color: '#ef4444', marginLeft: 2 }}>*</span>}
                </label>
            )}
            
            {children}
            
            {error ? (
                <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>
                    {error}
                </div>
            ) : helperText ? (
                <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: 4 }}>
                    {helperText}
                </div>
            ) : null}
        </div>
    );
}
