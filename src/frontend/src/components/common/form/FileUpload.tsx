import React, { useRef, useState } from 'react';
import { Upload, X, FileText, Download } from 'lucide-react';
import { FormFieldWrapper, FormFieldWrapperProps } from './FormFieldWrapper';
import { ConfirmationDialog } from '../ConfirmationDialog';

export interface FileUploadProps extends Omit<FormFieldWrapperProps, 'children'> {
    file: File | null;
    existingFileUrl?: string;
    existingFileName?: string;
    onChange: (file: File | null) => void;
    onRemoveExisting?: () => void;
    accept?: string;
    maxSizeMB?: number;
    disabled?: boolean;
    requireConfirmation?: boolean;
}

export function FileUpload({
    label,
    required,
    error,
    helperText,
    className,
    style,
    file,
    existingFileUrl,
    existingFileName,
    onChange,
    onRemoveExisting,
    accept,
    maxSizeMB,
    disabled,
    requireConfirmation = true
}: FileUploadProps) {
    const inputRef = useRef<HTMLInputElement>(null);
    const [isDragging, setIsDragging] = useState(false);
    const [internalError, setInternalError] = useState<string | null>(null);
    const [showConfirmRemove, setShowConfirmRemove] = useState(false);

    const handleFile = (selectedFile: File) => {
        setInternalError(null);
        if (maxSizeMB && selectedFile.size > maxSizeMB * 1024 * 1024) {
            setInternalError(`O arquivo deve ter no máximo ${maxSizeMB}MB.`);
            return;
        }
        onChange(selectedFile);
    };

    const onFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            handleFile(e.target.files[0]);
        }
    };

    const handleDragOver = (e: React.DragEvent) => {
        e.preventDefault();
        if (!disabled) setIsDragging(true);
    };

    const handleDragLeave = (e: React.DragEvent) => {
        e.preventDefault();
        setIsDragging(false);
    };

    const handleDrop = (e: React.DragEvent) => {
        e.preventDefault();
        setIsDragging(false);
        if (disabled) return;
        
        if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            handleFile(e.dataTransfer.files[0]);
        }
    };

    const handleRemoveClick = (e: React.MouseEvent) => {
        e.stopPropagation();
        if (disabled) return;

        if (existingFileUrl && onRemoveExisting) {
            if (requireConfirmation) {
                setShowConfirmRemove(true);
            } else {
                onRemoveExisting();
                onChange(null);
            }
        } else {
            onChange(null);
        }
    };

    const confirmRemove = () => {
        if (onRemoveExisting) onRemoveExisting();
        onChange(null);
        setShowConfirmRemove(false);
    };

    const displayError = internalError || error;
    const hasFile = !!file || !!existingFileUrl;

    return (
        <FormFieldWrapper
            label={label}
            required={required}
            error={displayError}
            helperText={helperText}
            className={className}
            style={style}
        >
            <div
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                onClick={() => !hasFile && !disabled && inputRef.current?.click()}
                style={{
                    border: `1px dashed ${displayError ? '#ef4444' : isDragging ? '#3b82f6' : 'var(--color-border)'}`,
                    borderRadius: '8px',
                    padding: hasFile ? '12px' : '20px',
                    backgroundColor: disabled ? 'var(--color-bg-surface)' : isDragging ? '#eff6ff' : '#fafafa',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: hasFile ? 'space-between' : 'center',
                    cursor: hasFile || disabled ? 'default' : 'pointer',
                    transition: 'all 0.2s ease',
                    opacity: disabled ? 0.6 : 1,
                }}
            >
                <input
                    type="file"
                    ref={inputRef}
                    onChange={onFileChange}
                    accept={accept}
                    disabled={disabled}
                    style={{ display: 'none' }}
                />

                {!hasFile ? (
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
                        <Upload size={24} style={{ color: displayError ? '#ef4444' : 'var(--color-placeholder)' }} />
                        <div style={{ fontSize: '0.85rem', color: 'var(--color-text-main)', textAlign: 'center' }}>
                            <span style={{ fontWeight: 600, color: '#3b82f6' }}>Clique para escolher</span> ou arraste o arquivo aqui
                        </div>
                        {maxSizeMB && (
                            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                Tamanho máximo: {maxSizeMB}MB
                            </div>
                        )}
                    </div>
                ) : (
                    <>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', overflow: 'hidden' }}>
                            <div style={{ padding: '8px', backgroundColor: '#eef2ff', borderRadius: '6px', color: '#4f46e5' }}>
                                <FileText size={20} />
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
                                <span style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', whiteSpace: 'nowrap', textOverflow: 'ellipsis', overflow: 'hidden' }}>
                                    {file ? file.name : existingFileName || 'Documento existente'}
                                </span>
                                {file && (
                                    <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                        {(file.size / 1024 / 1024).toFixed(2)} MB
                                    </span>
                                )}
                            </div>
                        </div>

                        <div style={{ display: 'flex', gap: '8px' }}>
                            {existingFileUrl && (
                                <a
                                    href={existingFileUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    onClick={(e) => e.stopPropagation()}
                                    style={{
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        width: '32px', height: '32px', borderRadius: '6px',
                                        backgroundColor: '#ffffff', border: '1px solid var(--color-border)',
                                        color: '#3b82f6', cursor: 'pointer'
                                    }}
                                    title="Visualizar arquivo"
                                >
                                    <Download size={16} />
                                </a>
                            )}
                            {!disabled && (
                                <button
                                    type="button"
                                    onClick={handleRemoveClick}
                                    style={{
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        width: '32px', height: '32px', borderRadius: '6px',
                                        backgroundColor: '#fef2f2', border: '1px solid #fecaca',
                                        color: '#ef4444', cursor: 'pointer'
                                    }}
                                    title="Remover"
                                >
                                    <X size={16} />
                                </button>
                            )}
                        </div>
                    </>
                )}
            </div>

            {showConfirmRemove && (
                <ConfirmationDialog
                    title="Remover Documento"
                    message={file ? "Deseja remover o arquivo selecionado?" : "Deseja realmente remover este documento existente? Esta ação não pode ser desfeita e exigirá que você salve as alterações."}
                    confirmText="Remover"
                    variant="destructive"
                    onConfirm={confirmRemove}
                    onCancel={() => setShowConfirmRemove(false)}
                />
            )}
        </FormFieldWrapper>
    );
}
