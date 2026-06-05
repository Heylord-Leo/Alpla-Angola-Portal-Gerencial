import { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import { API_BASE_URL } from '../lib/api';

// ─── Types ───────────────────────────────────────────────────
export interface AppEnvironment {
    code: string;
    name: string;
    showBanner: boolean;
}

interface EnvironmentContextValue extends AppEnvironment {
    /** Convenience flag: true when code is not "PROD" */
    isTest: boolean;
    /** True while the initial fetch is in progress */
    loading: boolean;
}

const DEFAULT_PROD: AppEnvironment = {
    code: 'PROD',
    name: 'Produção',
    showBanner: false,
};

// ─── URL-based fallback detection ────────────────────────────
// Used ONLY when the backend endpoint is unreachable.
// Backend configuration is always the source of truth (DEC-140).
function detectFromUrl(): AppEnvironment {
    const hostname = window.location.hostname.toLowerCase();
    if (hostname.includes('-test') || hostname.includes('test.') || hostname === 'localhost') {
        return { code: 'TEST', name: 'Ambiente de Teste', showBanner: true };
    }
    return DEFAULT_PROD;
}

// ─── Context ─────────────────────────────────────────────────
const EnvironmentContext = createContext<EnvironmentContextValue>({
    ...DEFAULT_PROD,
    isTest: false,
    loading: true,
});

export function useEnvironment(): EnvironmentContextValue {
    return useContext(EnvironmentContext);
}

// ─── Provider ────────────────────────────────────────────────
export function EnvironmentProvider({ children }: { children: ReactNode }) {
    const [env, setEnv] = useState<AppEnvironment>(DEFAULT_PROD);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        let cancelled = false;

        (async () => {
            try {
                const res = await fetch(`${API_BASE_URL}/api/app/environment`);
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                const data: AppEnvironment = await res.json();
                if (!cancelled) setEnv(data);
            } catch {
                // Backend unreachable — fall back to URL detection
                console.info('[EnvironmentProvider] Backend unreachable, using URL-based fallback.');
                if (!cancelled) setEnv(detectFromUrl());
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();

        return () => { cancelled = true; };
    }, []);

    // Update document.title based on environment
    useEffect(() => {
        const isTest = env.code !== 'PROD';
        const baseTitle = 'Portal Gerencial';
        document.title = isTest ? `[TEST] ${baseTitle}` : baseTitle;
    }, [env.code]);

    const value: EnvironmentContextValue = {
        ...env,
        isTest: env.code !== 'PROD',
        loading,
    };

    return (
        <EnvironmentContext.Provider value={value}>
            {children}
        </EnvironmentContext.Provider>
    );
}
