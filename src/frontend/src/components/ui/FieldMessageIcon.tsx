import React, { useRef, useState } from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, Info } from 'lucide-react';
import { ModernTooltip } from './ModernTooltip';
import { InfoModal, InfoModalTone } from './InfoModal';

export type FieldMessageSeverity = 'info' | 'success' | 'warning' | 'error';

interface Props {
    severity: FieldMessageSeverity;
    /** One short sentence on hover. Never a paragraph — the modal is where explanation belongs. */
    tooltip: string;
    /** Modal heading. */
    title: string;
    /** Accessible name for the icon button. Defaults to the tooltip. */
    ariaLabel?: string;
    /** Full explanation, shown only inside the modal. */
    children: React.ReactNode;
    footer?: React.ReactNode;
    maxWidth?: number;
    /** Controlled mode: lets a parent open the modal itself (e.g. a conflict opening on selection). */
    isOpen?: boolean;
    onOpenChange?: (open: boolean) => void;
    /** A decision modal must not vanish on a stray outside click. */
    closeOnBackdrop?: boolean;
    size?: number;
}

const SEVERITY: Record<FieldMessageSeverity, { color: string; tone: InfoModalTone; Icon: typeof Info }> = {
    info: { color: '#2563eb', tone: 'info', Icon: Info },
    success: { color: '#15803d', tone: 'neutral', Icon: CheckCircle2 },
    warning: { color: '#b45309', tone: 'warning', Icon: AlertTriangle },
    error: { color: '#b91c1c', tone: 'danger', Icon: AlertCircle }
};

/**
 * A contextual message compressed to a single icon.
 *
 * <p>Every long explanatory block that used to sit under a narrow form field — the OCR reading, its
 * evidence, an expired document, a value filled in automatically — becomes one of these. The field
 * keeps a fixed height whatever the Portal has to say about it, which is the entire point: a form
 * that reflows every time a warning appears is a form that is hard to fill in.</p>
 *
 * <p>Severity is not decoration. It decides whether the user is being informed, cautioned or
 * blocked, and it is the only part of the message visible without interaction — so it has to be
 * chosen honestly.</p>
 *
 * <p>Inline text is still correct for one thing: a validation error the user must fix right now.
 * Those stay where they are.</p>
 */
export function FieldMessageIcon({
    severity,
    tooltip,
    title,
    ariaLabel,
    children,
    footer,
    maxWidth = 620,
    isOpen,
    onOpenChange,
    closeOnBackdrop = true,
    size = 14
}: Props) {
    const [internalOpen, setInternalOpen] = useState(false);
    const triggerRef = useRef<HTMLButtonElement>(null);

    const controlled = isOpen !== undefined;
    const open = controlled ? isOpen : internalOpen;

    const setOpen = (next: boolean) => {
        if (!controlled) setInternalOpen(next);
        onOpenChange?.(next);
    };

    const { color, tone, Icon } = SEVERITY[severity];

    return (
        <>
            <ModernTooltip side="top" maxWidth={280} triggerTabIndex={-1} content={tooltip}>
                <button
                    ref={triggerRef}
                    type="button"
                    // The field often renders inside a <label>; without preventDefault the click
                    // would also be forwarded to the labelled control.
                    onClick={e => { e.preventDefault(); e.stopPropagation(); setOpen(true); }}
                    aria-haspopup="dialog"
                    aria-label={ariaLabel ?? tooltip}
                    style={{
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                        padding: 0, background: 'none', border: 'none', cursor: 'pointer',
                        color, lineHeight: 0
                    }}
                >
                    <Icon size={size} />
                </button>
            </ModernTooltip>

            <InfoModal
                isOpen={open}
                onClose={() => setOpen(false)}
                title={title}
                icon={<Icon size={18} />}
                tone={tone}
                maxWidth={maxWidth}
                footer={footer}
                closeOnBackdrop={closeOnBackdrop}
            >
                {children}
            </InfoModal>
        </>
    );
}
