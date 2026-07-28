// Deterministic minimum-quality check for justification text (reconciliation SUBSTITUTE/
// EXTRA_ITEM/value-bearing IGNORED, and batch-composition EXCLUDE comments). This is a
// byte-for-byte port of the backend's ReconciliationJustificationValidator.cs — same three
// rules, same order, same thresholds, same Portuguese messages. Frontend validation is UX
// only; the backend remains authoritative (identical logic there is what actually enforces it).
//
// Backend reference: src/backend/AlplaPortal.Application/Validation/ReconciliationJustificationValidator.cs

/** Mirrors ReconciliationJustificationValidator.MinLength. */
export const RECONCILIATION_JUSTIFICATION_MIN_LENGTH = 20;

/** Mirrors ReconciliationJustificationValidator.MaxRepeatedCharRatio — longest run of one
 * repeated character, as a fraction of the trimmed text length, above which the text is
 * treated as a placeholder (e.g. "aaaaaaaaaaaaaaaaaaaaa", "okkkkkkkkkkkkkkkkkkkkkk"). */
const MAX_REPEATED_CHAR_RATIO = 0.6;

export interface JustificationValidationResult {
    isValid: boolean;
    /** Empty string when isValid is true. */
    error: string;
}

export function validateReconciliationJustification(justification: string | null | undefined): JustificationValidationResult {
    const trimmed = (justification ?? '').trim();

    if (trimmed.length < RECONCILIATION_JUSTIFICATION_MIN_LENGTH) {
        return { isValid: false, error: `A justificativa deve ter pelo menos ${RECONCILIATION_JUSTIFICATION_MIN_LENGTH} caracteres.` };
    }
    if (/^\d+$/.test(trimmed)) {
        return { isValid: false, error: 'A justificativa não pode conter apenas números.' };
    }
    if (hasExcessiveRepeatedCharacters(trimmed)) {
        return { isValid: false, error: 'A justificativa parece ser um texto de preenchimento (caracteres repetidos). Informe um motivo real.' };
    }
    return { isValid: true, error: '' };
}

/** Mirrors C#'s HasExcessiveRepeatedCharacters exactly: longest run of one repeated character
 * (case-insensitive, via toLowerCase like C#'s char.ToLowerInvariant), compared as a ratio of
 * the full trimmed length using strict `>` against the threshold. */
function hasExcessiveRepeatedCharacters(text: string): boolean {
    let longestRun = 1;
    let currentRun = 1;
    for (let i = 1; i < text.length; i++) {
        if (text[i].toLowerCase() === text[i - 1].toLowerCase()) {
            currentRun++;
            if (currentRun > longestRun) longestRun = currentRun;
        } else {
            currentRun = 1;
        }
    }
    return longestRun / text.length > MAX_REPEATED_CHAR_RATIO;
}
