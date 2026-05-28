import { Sparkles } from 'lucide-react';
import { useLiveGuideContext } from './LiveGuideProvider';
import type { LiveGuideId } from './liveGuideTypes';

/**
 * LiveGuideLauncher
 *
 * Inline button placed on pages that offer a Live Guide (interactive step-by-step).
 * Styled consistently with GuidedTourContextButton but with a distinct icon
 * to differentiate "interactive guide" from "explanatory tour".
 *
 * Usage:
 * ```tsx
 * <LiveGuideLauncher guideId="request-creation-live-guide" />
 * ```
 */
interface LiveGuideLauncherProps {
    /** The live guide to start when clicked */
    guideId: LiveGuideId;
    /** Optional custom label (defaults to "Guia ao vivo") */
    label?: string;
}

export function LiveGuideLauncher({ guideId, label = 'Guia ao vivo' }: LiveGuideLauncherProps) {
    const { startLiveGuide, isLiveGuideActive } = useLiveGuideContext();

    return (
        <button
            data-guide="live-guide-launcher"
            onClick={() => startLiveGuide(guideId)}
            title={label}
            disabled={isLiveGuideActive}
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                backgroundColor: isLiveGuideActive
                    ? 'rgba(var(--color-primary-rgb), 0.04)'
                    : 'rgba(var(--color-primary-rgb), 0.06)',
                color: isLiveGuideActive
                    ? 'var(--color-text-muted)'
                    : 'var(--color-primary)',
                border: `1px solid ${isLiveGuideActive
                    ? 'rgba(var(--color-primary-rgb), 0.08)'
                    : 'rgba(var(--color-primary-rgb), 0.15)'}`,
                padding: '6px 14px',
                borderRadius: '8px',
                fontWeight: 700,
                fontSize: '0.78rem',
                cursor: isLiveGuideActive ? 'not-allowed' : 'pointer',
                transition: 'all 0.2s',
                fontFamily: 'var(--font-family-display)',
                letterSpacing: '0.02em',
                whiteSpace: 'nowrap',
                opacity: isLiveGuideActive ? 0.6 : 1,
            }}
            onMouseEnter={(e) => {
                if (!isLiveGuideActive) {
                    e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.12)';
                    e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.25)';
                    e.currentTarget.style.transform = 'translateY(-1px)';
                }
            }}
            onMouseLeave={(e) => {
                if (!isLiveGuideActive) {
                    e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.06)';
                    e.currentTarget.style.borderColor = 'rgba(var(--color-primary-rgb), 0.15)';
                    e.currentTarget.style.transform = 'translateY(0)';
                }
            }}
        >
            <Sparkles size={14} strokeWidth={2.5} />
            {label}
        </button>
    );
}
