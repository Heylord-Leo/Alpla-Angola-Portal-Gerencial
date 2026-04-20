import { useState, useEffect } from 'react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend } from 'recharts';
import { api } from '../../../lib/api';
import { Loader2 } from 'lucide-react';

interface FinancialTrendPointDto {
  periodLabel: string;
  approvedAmount: number;
  approvedCount: number;
  paidAmount: number;
  paidCount: number;
}

interface ApprovalFinancialTrendDto {
  requestId: string;
  currency: string;
  resolution: string;
  scope: string;
  dataPoints: FinancialTrendPointDto[];
}

interface DecisionFinancialTrendLineProps {
  requestId: string;
}

export function DecisionFinancialTrendLine({ requestId }: DecisionFinancialTrendLineProps) {
  const [data, setData] = useState<ApprovalFinancialTrendDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [resolution, setResolution] = useState<'MONTH' | 'WEEK'>('MONTH');
  const [scope, setScope] = useState<'PLANT' | 'DEPARTMENT'>('DEPARTMENT');

  useEffect(() => {
    let mounted = true;

    const fetchTrend = async () => {
      try {
        setLoading(true);
        const fetchedData = await api.approvals.getFinanceTrend(requestId, resolution, scope);
        if (mounted) {
          setData(fetchedData);
        }
      } catch (err) {
        console.error('Failed to fetch financial trend:', err);
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    fetchTrend();

    return () => {
      mounted = false;
    };
  }, [requestId, resolution, scope]);

  const formatCurrency = (value: number) => {
    return new Intl.NumberFormat('pt-AO', {
      style: 'currency',
      currency: data?.currency || 'AOA',
      maximumFractionDigits: 0
    }).format(value);
  };

  return (
    <div style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      {/* Controls */}
      <div style={{ 
        display: 'flex', 
        flexWrap: 'wrap', 
        justifyContent: 'space-between', 
        alignItems: 'center', 
        gap: '1rem', 
        backgroundColor: 'var(--color-bg-page)', 
        border: '1px solid #F3F4F6', 
        padding: '0.75rem', 
        borderRadius: '0.375rem' 
      }}>
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <button
            onClick={() => setResolution('WEEK')}
            style={{
              padding: '0.25rem 0.75rem',
              fontSize: '0.75rem',
              fontWeight: 500,
              borderRadius: '0.25rem',
              cursor: 'pointer',
              transition: 'all 0.2s',
              ...(resolution === 'WEEK' 
                ? { backgroundColor: '#1E40AF', color: '#FFFFFF', border: '1px solid #1E40AF' }
                : { backgroundColor: 'var(--color-bg-surface)', color: '#4B5563', border: '1px solid #E5E7EB' })
            }}
          >
            Semanas
          </button>
          <button
            onClick={() => setResolution('MONTH')}
            style={{
              padding: '0.25rem 0.75rem',
              fontSize: '0.75rem',
              fontWeight: 500,
              borderRadius: '0.25rem',
              cursor: 'pointer',
              transition: 'all 0.2s',
              ...(resolution === 'MONTH' 
                ? { backgroundColor: '#1E40AF', color: '#FFFFFF', border: '1px solid #1E40AF' }
                : { backgroundColor: 'var(--color-bg-surface)', color: '#4B5563', border: '1px solid #E5E7EB' })
            }}
          >
            Meses
          </button>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <button
            onClick={() => setScope('PLANT')}
            style={{
              padding: '0.25rem 0.75rem',
              fontSize: '0.75rem',
              fontWeight: 500,
              borderRadius: '0.25rem',
              cursor: 'pointer',
              transition: 'all 0.2s',
              ...(scope === 'PLANT' 
                ? { backgroundColor: '#1E40AF', color: '#FFFFFF', border: '1px solid #1E40AF' }
                : { backgroundColor: 'var(--color-bg-surface)', color: '#4B5563', border: '1px solid #E5E7EB' })
            }}
          >
            Apenas Planta Atual
          </button>
          <button
            onClick={() => setScope('DEPARTMENT')}
            style={{
              padding: '0.25rem 0.75rem',
              fontSize: '0.75rem',
              fontWeight: 500,
              borderRadius: '0.25rem',
              cursor: 'pointer',
              transition: 'all 0.2s',
              ...(scope === 'DEPARTMENT' 
                ? { backgroundColor: '#1E40AF', color: '#FFFFFF', border: '1px solid #1E40AF' }
                : { backgroundColor: 'var(--color-bg-surface)', color: '#4B5563', border: '1px solid #E5E7EB' })
            }}
          >
            Apenas este Departamento
          </button>
        </div>
      </div>

      {/* Chart Area */}
      <div style={{ height: '300px', width: '100%', position: 'relative' }}>
        {loading && (
          <div style={{
            position: 'absolute',
            inset: 0,
            zIndex: 10,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: 'rgba(255,255,255,0.7)',
            borderRadius: '0.375rem'
          }}>
            <Loader2 size={24} style={{ color: '#1E40AF', animation: 'spin 1s linear infinite' }} />
          </div>
        )}

        {!loading && (!data || data.dataPoints.length === 0) && (
          <div style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            border: '1px dashed #E5E7EB',
            borderRadius: '0.375rem',
            backgroundColor: 'var(--color-bg-page)',
            color: '#6B7280',
            fontSize: '0.875rem'
          }}>
            Não há histórico suficiente neste recorte temporal para montar o gráfico.
          </div>
        )}

        {data && data.dataPoints.length > 0 && (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart
              data={data.dataPoints}
              margin={{ top: 10, right: 10, left: 20, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#E5E7EB" />
              <XAxis 
                dataKey="periodLabel" 
                axisLine={false}
                tickLine={false}
                tick={{ fontSize: 12, fill: '#6B7280' }}
                dy={10}
              />
              <YAxis 
                tickFormatter={(value) => {
                  if (value >= 1000000) return (value / 1000000).toFixed(1) + 'M';
                  if (value >= 1000) return (value / 1000).toFixed(0) + 'k';
                  return value;
                }}
                axisLine={false}
                tickLine={false}
                tick={{ fontSize: 12, fill: '#6B7280' }}
              />
              <Tooltip 
                content={({ active, payload, label }) => {
                  if (active && payload && payload.length) {
                    const point = payload[0].payload as FinancialTrendPointDto;
                    return (
                      <div style={{ 
                        backgroundColor: 'var(--color-bg-surface)', 
                        padding: '12px', 
                        borderRadius: '8px', 
                        boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1)',
                        border: '1px solid #E5E7EB'
                      }}>
                        <p style={{ color: '#374151', fontWeight: 600, marginBottom: '8px', fontSize: '14px' }}>{label}</p>
                        {payload.map((entry: any, index: number) => {
                          const count = entry.dataKey === 'approvedAmount' ? point.approvedCount : point.paidCount;
                          return (
                            <div key={index} style={{ color: entry.color, marginBottom: '4px', fontSize: '14px' }}>
                              <span style={{ fontWeight: 500 }}>{entry.name}:</span> {formatCurrency(Number(entry.value))}
                              <span style={{ fontSize: '12px', opacity: 0.8, marginLeft: '4px' }}>
                                ({count} {count === 1 ? 'pedido' : 'pedidos'})
                              </span>
                            </div>
                          );
                        })}
                      </div>
                    );
                  }
                  return null;
                }}
              />
              <Legend 
                verticalAlign="top" 
                height={36} 
                iconType="circle"
                wrapperStyle={{ fontSize: '13px', paddingTop: '4px' }}
              />
              <Line 
                type="monotone" 
                name="Aprovados (Pendentes de Pagamento)" 
                dataKey="approvedAmount" 
                stroke="#F59E0B" // Amber-500
                strokeWidth={3}
                dot={{ r: 4, strokeWidth: 2 }}
                activeDot={{ r: 6 }}
              />
              <Line 
                type="monotone" 
                name="Pagos" 
                dataKey="paidAmount" 
                stroke="#3B82F6" // Blue-500
                strokeWidth={3}
                dot={{ r: 4, strokeWidth: 2 }}
                activeDot={{ r: 6 }}
              />
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  );
}
