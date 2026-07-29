import { useEffect, useRef, useState, useCallback } from 'react';
import { RefreshCw, AlertTriangle } from 'lucide-react';
import { useVersion } from '../../contexts/VersionContext';
import { versionSignal } from '../../lib/versionSignal';
import { hasUnsavedWork, markReloadAttempted, reloadAlreadyAttempted } from '../../lib/reloadGuard';

// ============================================================================
// Blocking "update required" modal (Phase D).
//
// Shown when the frontend is detected as outdated (version mismatch, a 409 CLIENT_VERSION_OUTDATED,
// or a stale lazy-chunk failure). It is BLOCKING and non-dismissible: no backdrop close, no Escape.
// Portuguese, dark-mode-aware (CSS variables), role="alertdialog", focus-trapped. It never reloads on
// top of an active mutation/upload or unsaved work without a deliberate confirmation.
// ============================================================================

const TITLE = 'Nova versão disponível';
const BODY_VERSION = 'O Portal Gerencial foi atualizado enquanto esta página estava aberta. Para continuar com segurança, atualize a página.';
const BODY_CHUNK = 'Não foi possível carregar parte da aplicação porque o Portal foi atualizado. Atualize a página para continuar.';
const PRIMARY = 'ATUALIZAR AGORA';

export function UpdateRequiredModal() {
    const { updateRequired, reason } = useVersion();
    const [confirmingUnsaved, setConfirmingUnsaved] = useState(false);
    const primaryRef = useRef<HTMLButtonElement>(null);
    const dialogRef = useRef<HTMLDivElement>(null);

    const doReload = useCallback(() => {
        markReloadAttempted();
        // With `index.html` served no-cache (Phase G), a normal reload fetches the fresh shell and
        // preserves the current route. No cache-busting query params (they would pollute routing).
        window.location.reload();
    }, []);

    const onPrimary = useCallback(() => {
        const risky = hasUnsavedWork() || versionSignal.hasActiveWrites();
        if (risky && !confirmingUnsaved) {
            setConfirmingUnsaved(true);
            return;
        }
        doReload();
    }, [confirmingUnsaved, doReload]);

    // Focus trap + block Escape while the modal is open.
    useEffect(() => {
        if (!updateRequired) return;
        primaryRef.current?.focus();

        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); return; }
            if (e.key === 'Tab') {
                const root = dialogRef.current;
                if (!root) return;
                const focusables = root.querySelectorAll<HTMLElement>('button, [href], input, [tabindex]:not([tabindex="-1"])');
                if (focusables.length === 0) return;
                const first = focusables[0];
                const last = focusables[focusables.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            }
        };
        document.addEventListener('keydown', onKeyDown, true);
        return () => document.removeEventListener('keydown', onKeyDown, true);
    }, [updateRequired]);

    if (!updateRequired) return null;

    const loopedChunk = reason === 'chunk' && reloadAlreadyAttempted();
    const body = reason === 'chunk' ? BODY_CHUNK : BODY_VERSION;

    return (
        <div
            role="presentation"
            style={{
                position: 'fixed', inset: 0,
                backgroundColor: 'rgba(17, 24, 39, 0.7)',
                backdropFilter: 'blur(4px)',
                zIndex: 100000,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                padding: '24px',
            }}
        >
            <div
                ref={dialogRef}
                role="alertdialog"
                aria-modal="true"
                aria-labelledby="update-modal-title"
                aria-describedby="update-modal-body"
                onClick={(e) => e.stopPropagation()}
                style={{
                    backgroundColor: 'var(--color-bg-surface, #ffffff)',
                    color: 'var(--color-text-main, #111827)',
                    border: '1px solid var(--color-border, #e5e7eb)',
                    borderRadius: 'var(--radius-lg, 12px)',
                    width: '100%', maxWidth: '460px',
                    boxShadow: '0 25px 50px -12px rgba(0,0,0,0.35)',
                    overflow: 'hidden',
                }}
            >
                <div style={{ padding: '24px 24px 8px', display: 'flex', alignItems: 'flex-start', gap: 14 }}>
                    <div style={{
                        width: 44, height: 44, borderRadius: '50%', flexShrink: 0,
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        backgroundColor: 'rgba(var(--color-primary-rgb, 0 77 144), 0.12)',
                        color: 'var(--color-primary, #004d90)',
                    }}>
                        <RefreshCw size={22} />
                    </div>
                    <div style={{ flex: 1 }}>
                        <h2 id="update-modal-title" style={{ margin: 0, fontSize: '1.1rem', fontWeight: 800, color: 'var(--color-text-main, #111827)' }}>
                            {TITLE}
                        </h2>
                        <p id="update-modal-body" style={{ margin: '8px 0 0', fontSize: '0.9rem', lineHeight: 1.55, color: 'var(--color-text-muted, #6b7280)' }}>
                            {body}
                        </p>
                    </div>
                </div>

                {(confirmingUnsaved || loopedChunk) && (
                    <div style={{
                        margin: '8px 24px 0', padding: '12px 14px', borderRadius: 8,
                        display: 'flex', gap: 10, alignItems: 'flex-start',
                        backgroundColor: 'rgba(217, 119, 6, 0.10)',
                        border: '1px solid rgba(217, 119, 6, 0.35)',
                        color: '#b45309',
                    }}>
                        <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: 2 }} />
                        <span style={{ fontSize: '0.82rem', fontWeight: 600, lineHeight: 1.5 }}>
                            {confirmingUnsaved
                                ? 'Há uma ação em andamento ou alterações não salvas nesta página. Se atualizar agora, esses dados serão perdidos. Confirme para atualizar mesmo assim, ou verifique o resultado após atualizar.'
                                : 'A atualização já foi tentada para esta versão. Se o problema persistir, atualize manualmente ou contacte o suporte.'}
                        </span>
                    </div>
                )}

                <div style={{ padding: '16px 24px 22px', display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
                    <button
                        ref={primaryRef}
                        onClick={onPrimary}
                        style={{
                            padding: '11px 22px', borderRadius: 8, border: 'none', cursor: 'pointer',
                            backgroundColor: 'var(--color-primary, #004d90)', color: '#ffffff',
                            fontWeight: 800, fontSize: '0.82rem', letterSpacing: '0.03em',
                            textTransform: 'uppercase', display: 'inline-flex', alignItems: 'center', gap: 8,
                            boxShadow: '0 2px 8px rgba(0,0,0,0.2)',
                        }}
                    >
                        <RefreshCw size={15} />
                        {confirmingUnsaved ? 'Atualizar mesmo assim' : PRIMARY}
                    </button>
                </div>
            </div>
        </div>
    );
}
