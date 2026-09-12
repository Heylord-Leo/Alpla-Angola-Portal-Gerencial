// v2.244.0 Approval Center V2 — Phase 3. Human-readable duration formatter for analytics.
// Renders seconds as "15 min", "2h 35m", "1d 4h", "3d 12h" (never raw seconds in the UI).

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null || Number.isNaN(seconds)) return '—';
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s}s`;

  const mins = Math.floor(s / 60);
  if (mins < 60) return `${mins} min`;

  const hours = Math.floor(mins / 60);
  const remMin = mins % 60;
  if (hours < 24) return remMin > 0 ? `${hours}h ${remMin}m` : `${hours}h`;

  const days = Math.floor(hours / 24);
  const remHours = hours % 24;
  return remHours > 0 ? `${days}d ${remHours}h` : `${days}d`;
}
