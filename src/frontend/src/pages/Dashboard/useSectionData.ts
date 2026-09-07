import { useCallback, useEffect, useRef, useState } from 'react';

// Shared async-state hook for independently-fetched Dashboard V2 sections. It distinguishes the three
// states a section needs to render honestly — 'loading' (request in flight), 'error' (request failed) and
// 'ready' (resolved; the section then decides entitled / empty / content from `data`). `retry` refetches
// ONLY this section, so one section's failure never affects the others.
//
// The fetcher receives an AbortSignal, and effect cleanup aborts the in-flight request (not merely
// ignores its response). This cancels the discarded request that React.StrictMode's DEV mount/cleanup/
// mount replay would otherwise leave running on the backend. An aborted request is expected lifecycle,
// never an error state; a stale aborted response can never overwrite the current data.
export type SectionStatus = 'loading' | 'error' | 'ready';

export interface SectionState<T> {
  status: SectionStatus;
  data: T | null;
  retry: () => void;
}

function isAbort(e: unknown): boolean {
  return !!e && typeof e === 'object' && (e as { name?: string }).name === 'AbortError';
}

export function useSectionData<T>(fetcher: (signal: AbortSignal) => Promise<T>): SectionState<T> {
  const [status, setStatus] = useState<SectionStatus>('loading');
  const [data, setData] = useState<T | null>(null);
  const [nonce, setNonce] = useState(0);

  // Keep the latest fetcher without making it an effect dependency (avoids a refetch loop from the
  // fetcher's changing identity each render). Refetch is driven explicitly by `nonce`.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  useEffect(() => {
    const controller = new AbortController();
    setStatus('loading');
    fetcherRef.current(controller.signal)
      .then((d) => { if (!controller.signal.aborted) { setData(d); setStatus('ready'); } })
      .catch((e) => {
        // Cancellation (cleanup/StrictMode replay) is not a failure — leave the state as-is.
        if (controller.signal.aborted || isAbort(e)) return;
        setData(null); setStatus('error');
      });
    return () => controller.abort();
  }, [nonce]);

  const retry = useCallback(() => setNonce((n) => n + 1), []);
  return { status, data, retry };
}
