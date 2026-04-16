import React from 'react';
import { User } from 'lucide-react';

/**
 * Badge data contract — data-minimized for V1.
 * Contains only the fields needed for badge rendering.
 */
export interface BadgeData {
    firstName?: string;
    lastName?: string;
    fullName?: string;
    department?: string;
    category?: string;
    employeeCode: string;
    cardNumber?: string;
    company?: string;
    photoUrl?: string | null;
    /** For future layout expansion */
    layoutId?: string;
}

interface BadgePreviewProps {
    data: BadgeData | null;
    /** Optional ref to attach for printing */
    printRef?: React.Ref<HTMLDivElement>;
    /** For future layout expansion */
    layoutConfig?: any;
}

/**
 * BadgePreview — Corporate badge layout (V1).
 *
 * Renders a CR-80 standard ID card (85.6mm × 53.98mm) preview.
 * Uses the company logo header, employee photo, identity info,
 * and footer identifiers.
 *
 * Design: Clean, professional, print-optimized.
 * Future: Will support multiple layout IDs and configuration objects.
 */
export function BadgePreview({ data, printRef, layoutConfig }: BadgePreviewProps) {
    if (!data) {
        return (
            <div style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                width: '340px', height: '214px',
                border: '2px dashed var(--color-border)', borderRadius: '10px',
                color: 'var(--color-text-muted)', fontSize: '0.85rem',
                fontWeight: 500, textAlign: 'center', padding: '24px'
            }}>
                Selecione um funcionário para visualizar o crachá
            </div>
        );
    }

    const displayName = data.firstName && data.lastName
        ? `${data.firstName} ${data.lastName}`
        : data.fullName || data.firstName || '—';

    const companyLabel = resolveCompanyLabel(data.company);

    return (
        <div ref={printRef} className="hr-badge-print-area">
            <div className="badge-card">
                {/* Header — Company branding */}
                <div className="badge-header">
                    <img src="/logo-v2.png" alt="Company Logo" />
                    <span className="company-name">{companyLabel}</span>
                </div>

                {/* Body — Photo + Identity */}
                <div className="badge-body">
                    <div className="badge-photo">
                        {data.photoUrl ? (
                            <img src={data.photoUrl} alt={displayName} />
                        ) : (
                            <User size={36} color="#9ca3af" />
                        )}
                    </div>
                    <div className="badge-info">
                        <div className="badge-name" title={displayName}>
                            {displayName}
                        </div>
                        {data.department && (
                            <div className="badge-department">{data.department}</div>
                        )}
                        {data.category && (
                            <div className="badge-category">{data.category}</div>
                        )}
                    </div>
                </div>

                {/* Footer — Identifiers */}
                <div className="badge-footer">
                    <div className="badge-footer-item">
                        <span className="badge-footer-label">Nº Func.</span>
                        <span className="badge-footer-value">{data.employeeCode}</span>
                    </div>
                    <div className="badge-footer-item">
                        <span className="badge-footer-label">Cartão</span>
                        <span className="badge-footer-value">{data.cardNumber || '—'}</span>
                    </div>
                    <div className="badge-footer-item">
                        <span className="badge-footer-label">Empresa</span>
                        <span className="badge-footer-value">{companyLabel}</span>
                    </div>
                </div>
            </div>
        </div>
    );
}

/**
 * Resolves Primavera company codes to display-friendly labels.
 */
function resolveCompanyLabel(company?: string): string {
    if (!company) return '—';

    const labels: Record<string, string> = {
        'ALPLAPLASTICO': 'Alpla Plástico',
        'ALPLASOPRO': 'Alpla Sopro'
    };

    return labels[company.toUpperCase()] || company;
}
