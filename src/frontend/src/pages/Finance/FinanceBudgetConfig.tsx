import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Save, AlertTriangle, AlertCircle, CheckCircle } from 'lucide-react';

interface Department {
    id: number;
    name: string;
}

interface Currency {
    id: number;
    code: string;
}

interface BudgetConfig {
    departmentId: number;
    totalAmount: number;
    currencyId: number;
    year: number;
}

export default function FinanceBudgetConfig() {
    const [departments, setDepartments] = useState<Department[]>([]);
    const [currencies, setCurrencies] = useState<Currency[]>([]);
    const [configs, setConfigs] = useState<Record<number, BudgetConfig>>({});
    
    const [year, setYear] = useState(new Date().getFullYear());
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null);

    useEffect(() => {
        loadBaseData();
    }, []);

    useEffect(() => {
        if (departments.length > 0 && currencies.length > 0) {
            loadConfigs(year);
        }
    }, [year, departments, currencies]);

    const loadBaseData = async () => {
        try {
            const [depsRes, curRes] = await Promise.all([
                api.lookups.getDepartments(),
                api.lookups.getCurrencies()
            ]);
            setDepartments(depsRes.filter(d => !d.locked)); // Use active or all depending on API, skipping inactive
            setCurrencies(curRes);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar dados base.' });
        }
    };

    const loadConfigs = async (targetYear: number) => {
        setLoading(true);
        try {
            const data = await api.financeBudget.getConfig(targetYear);
            const configsMap: Record<number, BudgetConfig> = {};
            data.forEach((item: any) => {
                configsMap[item.departmentId] = {
                    departmentId: item.departmentId,
                    totalAmount: item.totalAmount,
                    currencyId: item.currencyId,
                    year: targetYear
                };
            });
            setConfigs(configsMap);
            setLoading(false);
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao carregar configurações.' });
            setLoading(false);
        }
    };

    const handleFormChange = (departmentId: number, field: 'totalAmount' | 'currencyId', value: any) => {
        setConfigs(prev => {
            const existing = prev[departmentId] || { departmentId, year, totalAmount: 0, currencyId: currencies[0]?.id || 0 };
            return {
                ...prev,
                [departmentId]: { ...existing, [field]: value }
            };
        });
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            // Build the payload
            const payload = Object.values(configs).filter(c => c.totalAmount > 0);
            
            await api.financeBudget.saveConfig(payload);
            setMessage({ type: 'success', text: 'Orçamentos anuais guardados com sucesso.' });
            await loadConfigs(year); // Refresh to ensure backend normalization
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message || 'Erro ao salvar orçamentos.' });
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="max-w-4xl mx-auto p-4 md:p-8">
            <div className="flex justify-between items-center mb-6">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">Finance Budget Config</h1>
                    <p className="text-sm text-gray-500 mt-1">
                        Defina o orçamento anual (Committed Spend limits) aprovado para cada departamento.
                    </p>
                </div>
                <div className="flex items-center gap-4">
                    <div className="flex items-center gap-2 bg-white rounded-lg shadow-sm border border-gray-200 p-2">
                        <label className="text-sm font-medium text-gray-600 ml-1">Ano Fiscal:</label>
                        <select 
                            value={year}
                            onChange={(e) => setYear(parseInt(e.target.value))}
                            className="text-sm border-gray-300 rounded focus:ring-blue-500 focus:border-blue-500 bg-gray-50 p-1"
                        >
                            {[year - 1, year, year + 1, year + 2].map(y => (
                                <option key={y} value={y}>{y}</option>
                            ))}
                        </select>
                    </div>
                    <button
                        onClick={handleSave}
                        disabled={saving || loading}
                        className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 transition flex items-center justify-center gap-2 disabled:bg-blue-400 disabled:cursor-not-allowed shadow-sm font-medium"
                    >
                        <Save size={18} />
                        {saving ? 'A Guardar...' : 'Guardar Alterações'}
                    </button>
                </div>
            </div>

            {message && (
                <div className={`p-4 rounded-lg flex items-center gap-3 mb-6 ${message.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
                    {message.type === 'error' ? <AlertCircle size={20} /> : <CheckCircle size={20} />}
                    <span className="font-medium">{message.text}</span>
                </div>
            )}

            <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="w-full text-left border-collapse">
                        <thead>
                            <tr className="bg-gray-50 border-b border-gray-200">
                                <th className="p-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">Departamento</th>
                                <th className="p-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">Montante Anual</th>
                                <th className="p-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">Moeda (Base)</th>
                                <th className="p-4 text-xs font-semibold text-gray-600 uppercase tracking-wider text-right">Ação / Status</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan={4} className="p-8 text-center text-gray-500">A carregar...</td>
                                </tr>
                            ) : departments.length === 0 ? (
                                <tr>
                                    <td colSpan={4} className="p-8 text-center text-gray-500">Nenhum departamento ativo encontrado.</td>
                                </tr>
                            ) : (
                                departments.map(dept => {
                                    const conf = configs[dept.id];
                                    const hasData = conf && conf.totalAmount > 0;
                                    
                                    return (
                                        <tr key={dept.id} className="hover:bg-gray-50 transition-colors">
                                            <td className="p-4">
                                                <div className="font-medium text-gray-800">{dept.name}</div>
                                                <div className="text-xs text-gray-500">ID: {dept.id}</div>
                                            </td>
                                            <td className="p-4 w-48">
                                                <input 
                                                    type="number"
                                                    min="0"
                                                    step="0.01"
                                                    value={conf?.totalAmount || ''}
                                                    onChange={e => handleFormChange(dept.id, 'totalAmount', parseFloat(e.target.value) || 0)}
                                                    placeholder="Ex: 500000"
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500 text-sm"
                                                />
                                            </td>
                                            <td className="p-4 w-48">
                                                <select 
                                                    value={conf?.currencyId || (currencies[0]?.id || '')}
                                                    onChange={e => handleFormChange(dept.id, 'currencyId', parseInt(e.target.value))}
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500 text-sm bg-white"
                                                >
                                                    {currencies.map(c => (
                                                        <option key={c.id} value={c.id}>{c.code}</option>
                                                    ))}
                                                </select>
                                            </td>
                                            <td className="p-4 text-right">
                                                {hasData ? (
                                                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                                                        Configurado
                                                    </span>
                                                ) : (
                                                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
                                                        Não Atribuído
                                                    </span>
                                                )}
                                            </td>
                                        </tr>
                                    );
                                })
                            )}
                        </tbody>
                    </table>
                </div>
                
                <div className="bg-blue-50 p-4 border-t border-blue-100 text-sm text-blue-800 flex gap-2">
                    <AlertTriangle size={18} className="text-blue-600 flex-shrink-0" />
                    <p>
                        A alteração da moeda num orçamento existente recalculará os indicadores do painel de administração 
                        somente para pedidos com a nova moeda configurada (Agrupamento por Moeda Nativa). 
                        Garanta que a moeda selecionada reflete o acordo real de budget anual do departamento.
                    </p>
                </div>
            </div>
        </div>
    );
}
