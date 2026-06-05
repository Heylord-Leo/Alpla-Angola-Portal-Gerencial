import { useEnvironment } from '../../contexts/EnvironmentContext';

/**
 * Fixed top banner for non-PROD environments (DEC-140).
 * Renders an amber warning strip at the very top of the viewport.
 * Hidden in print via CSS class.
 *
 * Usage: Rendered once per page context:
 *   - Inside AppShell for authenticated pages
 *   - Directly in LoginPage / ResetPasswordPage for public pages
 */
export function EnvironmentBanner() {
    const { showBanner } = useEnvironment();

    if (!showBanner) return null;

    return (
        <div className="env-banner" role="status" aria-live="polite">
            <strong>AMBIENTE DE TESTE</strong>
            <span className="env-banner-separator">—</span>
            <span className="env-banner-text">
                Use apenas para validações e simulações. Dados e ações deste ambiente não representam o ambiente produtivo.
            </span>
        </div>
    );
}
