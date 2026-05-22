import { Compass } from 'lucide-react';
import { useGuidedTourContext } from './GuidedTourProvider';
import type { TourId } from './guidedTourTypes';

/**
 * GuidedTourContextButton
 * 
 * Inline button placed on specific pages (e.g., inside a PageHeader)
 * to launch a page-level or module-level tour directly.
 * 
 * Styled as a subtle icon+label button matching the Portal's
 * existing page-header action button patterns.
 */
interface GuidedTourContextButtonProps {
    /** The specific tour to launch */
    tourId: TourId;
    /** Optional custom label (defaults to "Tour da Tela") */
    label?: string;
}

export function GuidedTourContextButton({ tourId, label = 'Tour da Tela' }: GuidedTourContextButtonProps) {
    const { startTour } = useGuidedTourContext();

    return (
        <button
            data-tour="page-tour-button"
            onClick={() => startTour(tourId)}
            title={label}
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)',
                color: 'var(--color-primary)',
                border: '1px solid rgba(var(--color-primary-rgb), 0.15)',
                padding: '6px 14px',
                borderRadius: '8px',
                fontWeight: 700,
                fontSize: '0.78rem',
                cursor: 'pointer',
                transition: 'all 0.2s',
                fontFamily: 'var(--font-family-display)',
                letterSpacing: '0.02em',
                whiteSpace: 'nowrap',
            }}
            onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.12)';
                e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.25)';
                e.currentTarget.style.transform = 'translateY(-1px)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.06)';
                e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.15)';
                e.currentTarget.style.transform = 'translateY(0)';
            }}
        >
            <Compass size={14} strokeWidth={2.5} />
            {label}
        </button>
    );
}
