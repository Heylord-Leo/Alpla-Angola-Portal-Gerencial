import { useEffect, useCallback, useState } from 'react';

/**
 * BrowserRouter-compatible unsaved-changes protection hook.
 *
 * Provides:
 * 1. `beforeunload` listener — warns on browser refresh / tab close when dirty.
 * 2. `confirmNavigation(callback)` — wraps wizard-owned navigation actions
 *    (e.g. back button, breadcrumbs) with a confirmation gate.
 * 3. `showLeaveDialog` / dialog controls — drives the system ConfirmationDialog.
 *
 * This does NOT intercept sidebar/global SPA navigation (would require Data Router).
 * It only protects browser-level navigation and wizard-owned actions.
 */
export function useUnsavedChangesWarning({
    isDirty,
    isSubmitted,
}: {
    isDirty: boolean;
    isSubmitted: boolean;
}) {
    const [showLeaveDialog, setShowLeaveDialog] = useState(false);
    const [pendingNavigation, setPendingNavigation] = useState<(() => void) | null>(null);

    const shouldBlock = isDirty && !isSubmitted;

    // ── beforeunload: protect against browser refresh / tab close ──
    useEffect(() => {
        if (!shouldBlock) return;

        const handler = (e: BeforeUnloadEvent) => {
            e.preventDefault();
            // Modern browsers ignore custom messages but still show a generic prompt
            e.returnValue = '';
        };

        window.addEventListener('beforeunload', handler);
        return () => window.removeEventListener('beforeunload', handler);
    }, [shouldBlock]);

    // ── confirmNavigation: wrap wizard-owned navigation with confirmation ──
    const confirmNavigation = useCallback(
        (nextAction: () => void) => {
            if (!shouldBlock) {
                nextAction();
                return;
            }
            setPendingNavigation(() => nextAction);
            setShowLeaveDialog(true);
        },
        [shouldBlock]
    );

    // ── Dialog handlers ──
    const handleConfirmLeave = useCallback(() => {
        setShowLeaveDialog(false);
        if (pendingNavigation) {
            pendingNavigation();
            setPendingNavigation(null);
        }
    }, [pendingNavigation]);

    const handleCancelLeave = useCallback(() => {
        setShowLeaveDialog(false);
        setPendingNavigation(null);
    }, []);

    return {
        showLeaveDialog,
        confirmNavigation,
        handleConfirmLeave,
        handleCancelLeave,
    };
}
