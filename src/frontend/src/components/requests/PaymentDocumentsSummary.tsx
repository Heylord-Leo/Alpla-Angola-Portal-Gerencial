import React from 'react';
import { CompositionTotals } from '../../lib/paymentDocumentComposition';
import { FieldMessageIcon } from '../ui/FieldMessageIcon';
import { formatCurrencyAO } from '../../lib/utils';

interface Props {
    totals: CompositionTotals;
    /** The document still open in the editor, if any. Reported apart from the confirmed total. */
    provisional?: { sequence: number; gross: number; currency: string | null } | null;
}

/**
 * The consolidated value of the request.
 *
 * <p>Confirmed documents only. A document still being edited changes with every keystroke, and
 * folding it into the request total would show a number nobody agreed to; it is named separately as
 * provisional so the difference is visible rather than merged away.</p>
 *
 * <p>Nothing renders before the first document exists — a zero-value summary next to an empty form
 * tells the user their work was lost when in fact it never started.</p>
 */
export function PaymentDocumentsSummary({ totals, provisional }: Props) {
    if (totals.count === 0 && !provisional) return null;

    const currency = totals.currency ?? provisional?.currency ?? '';

    return (
        <section
            data-guide="request-documents-summary"
            aria-label="Resumo dos documentos"
            style={{
                display: 'flex', gap: '20px', flexWrap: 'wrap', alignItems: 'flex-end',
                padding: '12px 14px', borderRadius: 'var(--radius-sm, 8px)',
                backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)'
            }}
        >
            <h4 style={{
                margin: 0, alignSelf: 'center', fontSize: '0.7rem', fontWeight: 900,
                letterSpacing: '0.05em', textTransform: 'uppercase', color: 'var(--color-text-muted)'
            }}>
                Resumo dos documentos
            </h4>

            <Figure label="Documentos" value={String(totals.count)} />
            <Figure label="Valor líquido" value={`${formatCurrencyAO(totals.net)} ${currency}`} />
            <Figure label="IVA" value={`${formatCurrencyAO(totals.tax)} ${currency}`} />
            <Figure label="Total" value={`${formatCurrencyAO(totals.gross)} ${currency}`} strong />

            {provisional && (
                <Figure
                    label={`Documento ${provisional.sequence} (em revisão)`}
                    value={`${formatCurrencyAO(provisional.gross)} ${provisional.currency ?? currency}`}
                    muted
                />
            )}

            {(totals.count > 1 || provisional) && (
                <FieldMessageIcon
                    severity="info"
                    tooltip="O total do pedido é a soma dos documentos confirmados."
                    title="Total consolidado"
                    maxWidth={520}
                >
                    <p style={{ margin: 0, fontSize: '0.8125rem', lineHeight: 1.55 }}>
                        O valor do pedido é a soma dos documentos <strong>confirmados</strong>. Um documento
                        ainda em revisão é apresentado à parte e só passa a contar depois de o confirmar.
                    </p>
                    <p style={{ margin: '10px 0 0', fontSize: '0.8125rem', lineHeight: 1.55 }}>
                        Cada documento mantém a sua própria classificação e gera o seu próprio acompanhamento
                        depois do pagamento — documentos de plantas ou tipos diferentes não são agrupados.
                    </p>
                </FieldMessageIcon>
            )}
        </section>
    );
}

function Figure({ label, value, strong, muted }: {
    label: string; value: string; strong?: boolean; muted?: boolean;
}) {
    return (
        <span style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
            <span style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', fontWeight: 700 }}>
                {label}
            </span>
            <span style={{
                fontSize: strong ? '1rem' : '0.85rem',
                fontWeight: strong ? 900 : 600,
                color: muted ? 'var(--color-text-muted)' : 'var(--color-text-main)'
            }}>
                {value}
            </span>
        </span>
    );
}
