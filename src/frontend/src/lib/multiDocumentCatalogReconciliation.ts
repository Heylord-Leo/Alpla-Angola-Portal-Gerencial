import { ReconcilableItem, ItemResolution } from '../types';
import { TemporaryPaymentDocument, TemporaryPaymentItem } from './paymentRequestCreation';

/**
 * Catalogue reconciliation for a PAYMENT request that carries several source documents.
 *
 * <p>Reconciliation itself is not new. Before Release 3 every request ran its items past the Portal
 * catalogue before creation, and anything the catalogue did not recognise had to be linked to an
 * existing item or registered as a new one pending validation. What changed is only <b>where the
 * items live</b>: they used to sit in one grid on the request, and now each belongs to the document
 * it is billed on.</p>
 *
 * <p>So this module does one thing — present the items of several documents as the single flat list
 * the existing reconciliation UI already understands, and put the answers back exactly where they
 * came from. Everything here is pure.</p>
 */

// ── Normalisation ───────────────────────────────────────────────────────────────────────────

/**
 * Mirrors <c>CatalogItemReconciliationPolicy.NormalizeDescription</c> character for character.
 *
 * <p>Trim → lowercase → strip diacritics → collapse whitespace → drop trailing punctuation. The
 * backend applies exactly this before matching a description against the catalogue, so a line the
 * client calls "equivalent to one already resolved" is equivalent by the server's own rule rather
 * than by a description-equality rule invented here.</p>
 */
export function normalizeCatalogDescription(description: string | null | undefined): string {
    if (!description) return '';

    return description
        .trim()
        .toLowerCase()
        .normalize('NFD')
        // Unicode combining marks — the accents themselves, now detached by NFD.
        .replace(/[\u0300-\u036f]/g, '')
        .normalize('NFC')
        .replace(/\s+/g, ' ')
        .replace(/[.,;:!]+$/, '');
}

export function areCatalogDescriptionsEquivalent(
    left: string | null | undefined,
    right: string | null | undefined
): boolean {
    const a = normalizeCatalogDescription(left);
    // Two blank descriptions are not "the same item". An empty row has nothing to reconcile, and
    // treating them as equivalent would let one resolution silently claim every empty line.
    if (a.length === 0) return false;

    return a === normalizeCatalogDescription(right);
}

// ── The flat view the reconciliation UI consumes ────────────────────────────────────────────

/**
 * One line, carrying the document it belongs to.
 *
 * <p>Satisfies {@link ReconcilableItem}, so the existing hook and modal classify it with no change
 * to their rules. The extra fields are addressing information: they say which document and which
 * line an answer must be written back to, and they are never used to decide anything.</p>
 */
export interface DocumentScopedItem extends ReconcilableItem {
    description: string;
    itemCatalogId?: number | null;
    itemCatalogCode?: string | null;

    /** Identity of the owning document within the composition. Never an array position. */
    documentTempId: string;
    /** Identity of the line within that document. */
    itemTempId: string;

    /** What the user calls this document on screen. */
    documentSequence: number;
    /** The document's own number, shown alongside the sequence when it has one. */
    documentNumber: string | null;
}

/**
 * Every line of every confirmed document, in document order then line order.
 *
 * <p><b>Confirmed documents only.</b> A document still open in the editor is being worked on; its
 * lines change with every keystroke and are not yet part of the request. Reconciling them would ask
 * the user to settle the catalogue identity of a line they may be about to delete.</p>
 */
export function flattenConfirmedDocumentItems(
    documents: TemporaryPaymentDocument[]
): DocumentScopedItem[] {
    const flat: DocumentScopedItem[] = [];

    for (const document of documents.filter(d => d.confirmed)) {
        for (const item of document.items) {
            flat.push({
                description: item.description,
                itemCatalogId: item.itemCatalogId,
                itemCatalogCode: item.itemCatalogCode,
                documentTempId: document.tempId,
                itemTempId: item.tempId,
                documentSequence: document.localSequence,
                documentNumber: document.documentNumber
            });
        }
    }

    return flat;
}

/**
 * The same flat view, built from lines that already exist on a saved request.
 *
 * <p>Used by the editing path, where the items are rows in the database rather than entries in a
 * client-side composition. <c>itemTempId</c> carries the <b>persisted</b> line id, because that is
 * what a resolution has to be sent back against; the shape is otherwise identical, so every rule in
 * this module applies to a draft being edited exactly as it does to one being created.</p>
 *
 * <p>Only lines that belong to a source document are included. A multi-document request should have
 * no others, but a line without an owner is not something reconciliation should quietly adopt.</p>
 */
export function flattenPersistedLineItems(
    lineItems: Array<{
        id: string;
        description: string;
        itemCatalogId: number | null;
        itemCatalogCode: string | null;
        paymentSourceDocumentId?: string | null;
        paymentSourceDocumentSequence?: number | null;
        isDeleted?: boolean;
    }>
): DocumentScopedItem[] {
    return lineItems
        .filter(item => !item.isDeleted && !!item.paymentSourceDocumentId)
        .map(item => ({
            description: item.description,
            itemCatalogId: item.itemCatalogId,
            itemCatalogCode: item.itemCatalogCode,
            documentTempId: item.paymentSourceDocumentId!,
            itemTempId: item.id,
            documentSequence: item.paymentSourceDocumentSequence ?? 0,
            documentNumber: null
        }));
}

/**
 * The other still-unresolved lines that name the same catalogue item as the one at `index`.
 *
 * <p>Answers the question the reconciliation modal asks the moment a row is resolved: "does this
 * answer settle anything else?" Without it the modal insists on an answer for every row, so two
 * invoices billing <c>TRANSPORTE LOCAL</c> walk the user through the same decision twice — and if
 * they choose <i>Criar Novo</i> both times, the intent was two catalogue entries for one name.</p>
 */
export function equivalentUnresolvedIndexes(items: DocumentScopedItem[], index: number): number[] {
    const source = items[index];
    if (!source) return [];

    return items.reduce<number[]>((found, candidate, candidateIndex) => {
        if (candidateIndex === index) return found;
        if (candidate.itemCatalogId) return found;
        if (!areCatalogDescriptionsEquivalent(source.description, candidate.description)) return found;

        found.push(candidateIndex);
        return found;
    }, []);
}

/** The lines that still have to be answered for before the request may move on. */
export function unresolvedItems(items: DocumentScopedItem[]): DocumentScopedItem[] {
    return items.filter(item =>
        !item.itemCatalogId && normalizeCatalogDescription(item.description).length > 0);
}

/** "Documento 2 — FT-002", or just "Documento 2" when the document has no number yet. */
export function documentLabelOf(item: {
    documentSequence: number;
    documentNumber: string | null;
}): string {
    const number = item.documentNumber?.trim();
    return number ? `Documento ${item.documentSequence} — ${number}` : `Documento ${item.documentSequence}`;
}

/** Index → label, the shape the reconciliation modal takes for its Documento column. */
export function documentLabelsByIndex(items: DocumentScopedItem[]): Record<number, string> {
    const labels: Record<number, string> = {};
    items.forEach((item, index) => { labels[index] = documentLabelOf(item); });
    return labels;
}

// ── Reusing one answer across equivalent lines ──────────────────────────────────────────────

/**
 * Extends each resolution to the other unresolved lines that name the same catalogue item.
 *
 * <p>The case this exists for: two invoices in one request both billing <c>TRANSPORTE LOCAL</c>,
 * neither recognised by the catalogue. Without this the user is walked through creating the item
 * twice, and the catalogue ends up with two pending entries for one name — a mess somebody has to
 * clean up later, caused entirely by the request happening to carry two documents.</p>
 *
 * <p><b>Only the catalogue reference is shared.</b> The lines stay separate lines, each with its own
 * document, quantity, price and total; nothing here merges anything. Equivalence is decided by
 * {@link areCatalogDescriptionsEquivalent}, the same rule the automatic matcher uses.</p>
 *
 * <p>A line the user resolved explicitly is never overwritten — an explicit answer outranks an
 * inferred one, always.</p>
 */
export function propagateEquivalentResolutions(
    items: DocumentScopedItem[],
    resolutions: ItemResolution[]
): ItemResolution[] {
    const explicit = new Set(resolutions.map(r => r.itemIndex));
    const extra: ItemResolution[] = [];

    for (const resolution of resolutions) {
        if (!resolution.linkedCatalogId) continue;

        const source = items[resolution.itemIndex];
        if (!source) continue;

        items.forEach((candidate, index) => {
            if (explicit.has(index)) return;
            if (extra.some(e => e.itemIndex === index)) return;
            // Already linked — it has an answer, and it is not this one's business to change it.
            if (candidate.itemCatalogId) return;
            if (!areCatalogDescriptionsEquivalent(source.description, candidate.description)) return;

            extra.push({
                ...resolution,
                itemIndex: index,
                // However the source line was resolved, an equivalent line is being LINKED to the
                // resulting catalogue item — nothing is created a second time.
                status: 'LINKED_MANUALLY'
            });
        });
    }

    return [...resolutions, ...extra];
}

// ── Writing answers back ────────────────────────────────────────────────────────────────────

/**
 * Applies resolutions to the documents they came from.
 *
 * <p>The one invariant worth stating out loud: <b>only <c>itemCatalogId</c> and
 * <c>itemCatalogCode</c> are written</b>. The line keeps its document, its position in that
 * document, its quantity, unit, unit price, discount, IVA and total; the document keeps its
 * supplier, plant, type and everything else. Catalogue identity and payment-document ownership are
 * separate facts about a line, and reconciliation only ever decides the first.</p>
 *
 * <p>Addressing is by <c>tempId</c>, not by index, so a resolution cannot land on the wrong line if
 * anything about the composition shifted while the modal was open.</p>
 */
export function applyResolutionsToDocuments(
    documents: TemporaryPaymentDocument[],
    items: DocumentScopedItem[],
    resolutions: ItemResolution[]
): TemporaryPaymentDocument[] {
    /** itemTempId → the catalogue link it was given. */
    const byItem = new Map<string, { id: number; code: string | null }>();

    for (const resolution of resolutions) {
        const target = items[resolution.itemIndex];
        if (!target || !resolution.linkedCatalogId) continue;

        byItem.set(target.itemTempId, {
            id: resolution.linkedCatalogId,
            code: resolution.linkedCatalogCode ?? null
        });
    }

    if (byItem.size === 0) return documents;

    return documents.map(document => {
        if (!document.items.some(i => byItem.has(i.tempId))) return document;

        return {
            ...document,
            items: document.items.map((item): TemporaryPaymentItem => {
                const link = byItem.get(item.tempId);
                if (!link) return item;

                // Spread first, then the two catalogue fields. Every other value on the line
                // survives by construction rather than by being listed correctly.
                return { ...item, itemCatalogId: link.id, itemCatalogCode: link.code };
            })
        };
    });
}

/**
 * Folds the result of a catalogue batch-match into the flat list.
 *
 * <p>Runs before the user is asked anything. A line typed by hand never went through the automatic
 * matcher that OCR lines did, and telling somebody an item is "sem correspondência" when the
 * catalogue does in fact contain it is a false alarm that trains people to click past the warning.</p>
 */
export function applyAutoMatches(
    items: DocumentScopedItem[],
    matches: Record<number, { id: number; code: string } | null>
): DocumentScopedItem[] {
    return items.map((item, index) => {
        if (item.itemCatalogId) return item;

        const match = matches[index];
        if (!match) return item;

        return { ...item, itemCatalogId: match.id, itemCatalogCode: match.code };
    });
}

/** The lines a batch-match should be asked about: unlinked, and actually saying something. */
export function descriptionsNeedingMatch(items: DocumentScopedItem[]): Array<{ index: number; description: string }> {
    return items
        .map((item, index) => ({ index, description: item.description }))
        .filter(({ index, description }) =>
            !items[index].itemCatalogId && normalizeCatalogDescription(description).length > 0);
}
