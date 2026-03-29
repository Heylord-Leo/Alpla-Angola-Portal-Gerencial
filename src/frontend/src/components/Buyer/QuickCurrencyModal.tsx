import React, { useState, useEffect } from 'react';
import { X, Loader2 } from 'lucide-react';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../ui/Feedback';

interface QuickCurrencyModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: (currency: any) => void;
    initialCode: string;
}

export function QuickCurrencyModal({ isOpen, onClose, onSuccess, initialCode }: QuickCurrencyModalProps) {
    const [code, setCode] = useState('');
    const [symbol, setSymbol] = useState('');
    const [isSaving, setIsSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

    useEffect(() => {
        if (isOpen) {
            // Normalize initial code: trim, uppercase, and take first 3 chars if it looks like a code
            const normalized = initialCode.trim().toUpperCase();
            if (normalized.length === 3) {
                setCode(normalized);
            } else {
                setCode('');
            }
            setSymbol('');
            setFeedback({ type: 'success', message: null });
        }
    }, [isOpen, initialCode]);

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        
        const normalizedCode = code.trim().toUpperCase();
        
        if (!normalizedCode || normalizedCode.length !== 3) {
            setFeedback({ type: 'error', message: 'O código deve ter exatamente 3 caracteres (ex: USD).' });
            return;
        }

        setIsSaving(true);
        setFeedback({ type: 'success', message: null });

        try {
            const newCurrency = await api.lookups.createCurrency({
                code: normalizedCode,
                symbol: symbol.trim()
            });

            setFeedback({ type: 'success', message: 'Moeda criada com sucesso!' });
            
            // Short delay to show success
            setTimeout(() => {
                onSuccess(newCurrency);
                onClose();
            }, 500);
        } catch (err: any) {
            setFeedback({ 
                type: 'error', 
                message: err.message || 'Falha ao criar moeda. Verifique se o código já existe.' 
            });
        } finally {
            setIsSaving(false);
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm">
            <div className="bg-white rounded-xl shadow-2xl w-full max-w-md overflow-hidden">
                <div className="px-6 py-4 border-b border-gray-100 flex items-center justify-between bg-gray-50/50">
                    <h3 className="text-lg font-semibold text-gray-900">Nova Moeda</h3>
                    <button 
                        onClick={onClose}
                        className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-full transition-colors"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <form onSubmit={handleSave} className="p-6 space-y-4">
                    {feedback.message && (
                        <Feedback type={feedback.type} message={feedback.message} />
                    )}

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            Código (ISO) <span className="text-red-500">*</span>
                        </label>
                        <input
                            type="text"
                            value={code}
                            onChange={(e) => setCode(e.target.value.toUpperCase())}
                            placeholder="Ex: USD"
                            maxLength={3}
                            className="w-full px-4 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent outline-none transition-all uppercase"
                            required
                        />
                        <p className="mt-1 text-xs text-gray-500">Deve conter exatamente 3 letras.</p>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            Símbolo
                        </label>
                        <input
                            type="text"
                            value={symbol}
                            onChange={(e) => setSymbol(e.target.value)}
                            placeholder="Ex: $"
                            className="w-full px-4 py-2 border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent outline-none transition-all"
                        />
                    </div>

                    <div className="pt-4 flex items-center justify-end space-x-3">
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={isSaving}
                            className="px-4 py-2 text-sm font-medium text-gray-600 hover:text-gray-800 transition-colors"
                        >
                            Cancelar
                        </button>
                        <button
                            type="submit"
                            disabled={isSaving}
                            className="px-6 py-2 bg-blue-600 text-white text-sm font-semibold rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-all flex items-center"
                        >
                            {isSaving ? (
                                <>
                                    <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                                    Gravando...
                                </>
                            ) : (
                                'Criar Moeda'
                            )}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
