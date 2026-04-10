import { useEffect, useState } from 'react';

export type Theme = 'light' | 'dark' | 'system';

export function useTheme() {
    const [theme, setThemeState] = useState<Theme>(() => {
        const stored = localStorage.getItem('alpla-ui-theme') as Theme | null;
        return stored || 'system';
    });

    const setTheme = (newTheme: Theme) => {
        setThemeState(newTheme);
        localStorage.setItem('alpla-ui-theme', newTheme);
    };

    useEffect(() => {
        const root = document.documentElement;
        
        const applyTheme = (targetTheme: 'light' | 'dark') => {
            if (targetTheme === 'dark') {
                root.setAttribute('data-theme', 'dark');
            } else {
                root.removeAttribute('data-theme');
            }
        };

        if (theme === 'system') {
            const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
            applyTheme(mediaQuery.matches ? 'dark' : 'light');
            
            const listener = (e: MediaQueryListEvent) => applyTheme(e.matches ? 'dark' : 'light');
            mediaQuery.addEventListener('change', listener);
            return () => mediaQuery.removeEventListener('change', listener);
        } else {
            applyTheme(theme);
        }
    }, [theme]);

    return { theme, setTheme };
}
