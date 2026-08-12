import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import { FeatureFlagsDto } from '../types';

/**
 * Runtime feature flags from `GET /api/v1/config/features`.
 *
 * The Post-Payment Completion workflow is switched on through server configuration, not through an
 * Administration screen, so the UI has to ask. Two deliberate choices:
 *
 * - **Starts off.** The initial state has every flag false, so a screen renders its pre-feature
 *   layout on the first paint and never flashes a field that then disappears.
 * - **Fails off.** A failed request leaves the flags false and is swallowed. A configuration
 *   endpoint being briefly unavailable must never block request creation, and showing a mandatory
 *   field the server would not enforce is worse than showing nothing.
 */
const FLAGS_OFF: FeatureFlagsDto = {
    postPaymentCompletionEnabled: false,
    sourceDocumentTypeRequired: false,
    paymentMultiDocumentEnabled: false,
    completionLifecycleEnabled: false
};

export function useFeatureFlags() {
    const [flags, setFlags] = useState<FeatureFlagsDto>(FLAGS_OFF);
    const [isLoaded, setIsLoaded] = useState(false);

    useEffect(() => {
        let cancelled = false;

        (async () => {
            try {
                const result = await api.config.getFeatures();
                if (!cancelled) setFlags(result);
            } catch {
                // Fail off — see the note above.
                if (!cancelled) setFlags(FLAGS_OFF);
            } finally {
                if (!cancelled) setIsLoaded(true);
            }
        })();

        return () => { cancelled = true; };
    }, []);

    return { flags, isLoaded };
}
