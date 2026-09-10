// v2.242.0 — parse a FINANCE_RETURN_ADJUSTMENT history comment into a human-facing Finance message
// plus the low-emphasis technical context, so the Buyer immediately sees "what Finance said is wrong"
// instead of a wall of system metadata. Pure, presentation-only; never discards content.
//
// Backend comment format (FinanceController.ReturnForAdjustment):
//   "[Lote #N | <Supplier> | Moeda: <X> | Total: <Y>] Grupo devolvido por Finanças para correção da
//    P.O (GroupId: <guid>). Motivo: <human reason>"
// Legacy format:
//   "Devolvido por Finanças para ajuste: <human reason>"

export interface ParsedFinanceReturn {
  /** The prominent, human-entered Finance message. Always non-empty (neutral fallback when absent). */
  message: string;
  /** Low-emphasis system/technical context (bracket + system sentence), or null when there is none. */
  technical: string | null;
  /** True when a distinct human reason was actually present (false → neutral fallback was used). */
  hadReason: boolean;
}

const NEUTRAL = 'Finanças devolveu esta P.O. para correção.';
const LEGACY_PREFIX = 'Devolvido por Finanças para ajuste: ';

export function parseFinanceReturnComment(raw?: string | null): ParsedFinanceReturn {
  const text = (raw ?? '').trim();
  if (!text) return { message: NEUTRAL, technical: null, hadReason: false };

  // Legacy prefix: everything after it is the reason; no technical context.
  if (text.startsWith(LEGACY_PREFIX)) {
    const reason = text.substring(LEGACY_PREFIX.length).trim();
    return reason
      ? { message: reason, technical: null, hadReason: true }
      : { message: NEUTRAL, technical: null, hadReason: false };
  }

  // Canonical v2.242.0 format: split on the LAST "Motivo:" — the human reason follows it, everything
  // before it is system/technical context.
  const idx = text.lastIndexOf('Motivo:');
  if (idx >= 0) {
    const reason = text.substring(idx + 'Motivo:'.length).trim();
    const technical = text.substring(0, idx).trim();
    return {
      message: reason || NEUTRAL,
      technical: technical || null,
      hadReason: reason.length > 0,
    };
  }

  // No "Motivo:" — peel a leading "[ ... ]" technical bracket if present; keep the rest as the message.
  const bracket = text.match(/^\[[^\]]*\]\s*/);
  if (bracket) {
    const rest = text.substring(bracket[0].length).trim();
    return { message: rest || text, technical: bracket[0].trim(), hadReason: rest.length > 0 };
  }

  // Unknown shape — never discard: show the whole comment as the message.
  return { message: text, technical: null, hadReason: true };
}
