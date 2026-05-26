import { useState, useCallback, useRef, useEffect } from 'react';
import { useAuth } from '../features/auth/AuthContext';

// --- Schema version: bump this when the stored shape changes ---
const PREFERENCES_VERSION = 1;
const STORAGE_PREFIX = 'portal:prefs';
const DEBOUNCE_MS = 300;

// --- Public types ---

export interface TablePreferences {
  search?: string;
  filters?: Record<string, any>;
  sort?: { key: string; direction: 'asc' | 'desc' };
  pageSize?: number;
  viewMode?: string;
}

export interface UseTablePreferencesReturn {
  preferences: TablePreferences;
  setPreference: <K extends keyof TablePreferences>(key: K, value: TablePreferences[K]) => void;
  setPreferences: (updates: Partial<TablePreferences>) => void;
  resetPreferences: () => void;
  isHydrated: boolean;
}

// --- Internal stored shape ---

interface StoredEnvelope {
  _version: number;
  data: TablePreferences;
}

// --- Helpers ---

function buildKey(userId: string, screenKey: string): string {
  return `${STORAGE_PREFIX}:${userId}:${screenKey}`;
}

/**
 * Read and validate stored preferences.
 * Returns null if missing, corrupt, or version-mismatched.
 */
function readFromStorage(key: string): TablePreferences | null {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return null;

    const parsed: StoredEnvelope = JSON.parse(raw);

    // Version gate: discard outdated schemas
    if (!parsed || typeof parsed !== 'object' || parsed._version !== PREFERENCES_VERSION) {
      localStorage.removeItem(key);
      return null;
    }

    if (!parsed.data || typeof parsed.data !== 'object') {
      localStorage.removeItem(key);
      return null;
    }

    return parsed.data;
  } catch {
    // Corrupt JSON — silently discard
    try { localStorage.removeItem(key); } catch { /* noop */ }
    return null;
  }
}

/**
 * Strip empty/default-like values so we don't persist noise.
 */
function cleanPreferences(prefs: TablePreferences): TablePreferences | null {
  const cleaned: TablePreferences = {};
  let hasValue = false;

  if (prefs.search && prefs.search.trim().length > 0) {
    cleaned.search = prefs.search;
    hasValue = true;
  }

  if (prefs.filters && typeof prefs.filters === 'object') {
    const nonEmpty: Record<string, any> = {};
    for (const [k, v] of Object.entries(prefs.filters)) {
      if (v === undefined || v === null || v === '') continue;
      if (Array.isArray(v) && v.length === 0) continue;
      nonEmpty[k] = v;
    }
    if (Object.keys(nonEmpty).length > 0) {
      cleaned.filters = nonEmpty;
      hasValue = true;
    }
  }

  if (prefs.sort && prefs.sort.key) {
    cleaned.sort = prefs.sort;
    hasValue = true;
  }

  if (prefs.pageSize !== undefined && prefs.pageSize > 0) {
    cleaned.pageSize = prefs.pageSize;
    hasValue = true;
  }

  if (prefs.viewMode && prefs.viewMode.trim().length > 0) {
    cleaned.viewMode = prefs.viewMode;
    hasValue = true;
  }

  return hasValue ? cleaned : null;
}

function writeToStorage(key: string, prefs: TablePreferences): void {
  try {
    const cleaned = cleanPreferences(prefs);
    if (!cleaned) {
      // Nothing meaningful to store — remove the key entirely
      localStorage.removeItem(key);
      return;
    }
    const envelope: StoredEnvelope = { _version: PREFERENCES_VERSION, data: cleaned };
    localStorage.setItem(key, JSON.stringify(envelope));
  } catch {
    // localStorage full or unavailable — silently ignore
  }
}

function removeFromStorage(key: string): void {
  try { localStorage.removeItem(key); } catch { /* noop */ }
}

// --- Hook ---

export function useTablePreferences(
  screenKey: string,
  defaults: Partial<TablePreferences> = {}
): UseTablePreferencesReturn {
  const { user } = useAuth();
  const userId = user?.id;

  // Stable defaults ref to avoid re-renders from object identity changes
  const defaultsRef = useRef(defaults);
  defaultsRef.current = defaults;

  // Build the storage key (empty string if no user yet)
  const storageKey = userId ? buildKey(userId, screenKey) : '';

  // Lazy initializer: synchronous hydration on first render
  const [preferences, setPreferencesState] = useState<TablePreferences>(() => {
    if (!storageKey) return { ...defaults };
    const saved = readFromStorage(storageKey);
    if (saved) {
      return { ...defaults, ...saved };
    }
    return { ...defaults };
  });

  const [isHydrated, setIsHydrated] = useState(() => !!storageKey);

  // If userId becomes available after initial render (auth loading),
  // re-hydrate from localStorage once.
  const hasHydratedRef = useRef(!!storageKey);
  useEffect(() => {
    if (storageKey && !hasHydratedRef.current) {
      hasHydratedRef.current = true;
      const saved = readFromStorage(storageKey);
      if (saved) {
        setPreferencesState(() => ({ ...defaultsRef.current, ...saved }));
      }
      setIsHydrated(true);
    }
  }, [storageKey]);

  // Debounced write
  const writeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const scheduleWrite = useCallback((newPrefs: TablePreferences) => {
    if (!storageKey) return;
    if (writeTimerRef.current) clearTimeout(writeTimerRef.current);
    writeTimerRef.current = setTimeout(() => {
      writeToStorage(storageKey, newPrefs);
    }, DEBOUNCE_MS);
  }, [storageKey]);

  // Cleanup debounce timer on unmount
  useEffect(() => {
    return () => {
      if (writeTimerRef.current) clearTimeout(writeTimerRef.current);
    };
  }, []);

  const setPreference = useCallback(<K extends keyof TablePreferences>(
    key: K,
    value: TablePreferences[K]
  ) => {
    setPreferencesState(prev => {
      const next = { ...prev, [key]: value };
      scheduleWrite(next);
      return next;
    });
  }, [scheduleWrite]);

  const setPreferences = useCallback((updates: Partial<TablePreferences>) => {
    setPreferencesState(prev => {
      const next = { ...prev, ...updates };
      scheduleWrite(next);
      return next;
    });
  }, [scheduleWrite]);

  const resetPreferences = useCallback(() => {
    if (writeTimerRef.current) clearTimeout(writeTimerRef.current);
    if (storageKey) removeFromStorage(storageKey);
    setPreferencesState({ ...defaultsRef.current });
  }, [storageKey]);

  return {
    preferences,
    setPreference,
    setPreferences,
    resetPreferences,
    isHydrated,
  };
}
