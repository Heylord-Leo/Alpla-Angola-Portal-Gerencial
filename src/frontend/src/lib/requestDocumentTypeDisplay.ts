import { isSelectableDocumentType } from './sourceDocumentType';

/**
 * v2.245.9 — how the REQUEST-LEVEL "Tipo de documento anexado" (`Request.SourceDocumentType`) is
 * presented in the request header.
 *
 * Ownership model (proven in the repository, see CHANGELOG v2.245.9):
 *   • `Request.SourceDocumentType` is the document the requester DECLARED at creation (Release 2
 *     single-document PAYMENT model) — or, under the multi-document model, a compatibility echo of the
 *     documents that agree. It is editable only while the request is a DRAFT, and it seeds the group's
 *     classification once at Final Approval.
 *   • `RequestPoGroup.SourceDocumentType` is the OPERATIONAL classification — the authoritative source
 *     of the Final Invoice / Fiscal Receipt obligations, corrected through the v2.245.8 classification
 *     flow, and legitimately different from one group to another.
 *
 * Once operational groups exist the request-level value is therefore historical, non-authoritative
 * metadata. A legacy request (created before the feature) carries NO declaration at all — showing
 * "Não classificado" next to a classified group misreads a different concept as a pending state.
 */
export type RequestLevelDocumentTypeDisplay =
    /** DRAFT: the requester's declaration, editable (creation/edit flow unchanged). */
    | { mode: 'editable' }
    /** Read-only before any operational group exists: the declaration still seeds Final Approval. */
    | { mode: 'declaration' }
    /** Read-only with operational groups and a declared value: shown as the creation-time declaration. */
    | { mode: 'declared'; label: string; hint: string }
    /** Read-only with operational groups and NO declared value: suppressed — the group card is the truth. */
    | { mode: 'hidden' };

export const REQUEST_LEVEL_DECLARED_LABEL = 'Tipo de documento declarado no pedido';
export const REQUEST_LEVEL_DECLARED_HINT =
    'Declaração feita na criação do pedido. A classificação operacional que determina a Fatura Final e o ' +
    'Recibo Fiscal é a de cada grupo — ver a secção Fatura Final.';

export function resolveRequestLevelDocumentTypeDisplay(args: {
    status: string | null | undefined;
    hasOperationalGroups: boolean;
    value: string | null | undefined;
}): RequestLevelDocumentTypeDisplay {
    if (args.status === 'DRAFT') return { mode: 'editable' };
    if (!args.hasOperationalGroups) return { mode: 'declaration' };
    if (isSelectableDocumentType(args.value)) {
        return { mode: 'declared', label: REQUEST_LEVEL_DECLARED_LABEL, hint: REQUEST_LEVEL_DECLARED_HINT };
    }
    return { mode: 'hidden' };
}
