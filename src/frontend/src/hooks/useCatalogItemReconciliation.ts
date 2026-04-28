/**
 * useCatalogItemReconciliation
 *
 * Shared hook for classifying items by catalog reconciliation status.
 * Used across Payment Request, Requester Items, and Quotation Management flows.
 *
 * Responsibilities:
 *   - Classify each item as MATCHED, UNMATCHED, or track resolutions
 *   - Track modal open/close state
 *   - Provide resolution handlers (link, create-new, free-text)
 *   - Expose unresolved count for submission guardrails
 *
 * Internal constants use English. User-facing labels are in Portuguese.
 */
import { useState, useMemo, useCallback } from 'react';
import {
    ReconcilableItem,
    ClassifiedItem,
    ItemResolution,
} from '../types';

export interface UseReconciliationReturn<T extends ReconcilableItem> {
    /** All items with computed reconciliation status */
    classifiedItems: ClassifiedItem<T>[];
    /** Count of items needing resolution (UNMATCHED or LOW_CONFIDENCE) */
    unresolvedCount: number;
    /** Convenience boolean for guardrails */
    hasUnresolved: boolean;
    /** Open the reconciliation modal */
    openModal: () => void;
    /** Close the reconciliation modal */
    closeModal: () => void;
    /** Whether the modal is currently open */
    isModalOpen: boolean;
    /** Map of index → resolution for items resolved via the modal */
    resolutions: Map<number, ItemResolution>;
    /** Resolve a single item */
    resolveItem: (resolution: ItemResolution) => void;
    /** Resolve multiple items in batch */
    resolveAll: (resolutions: ItemResolution[]) => void;
    /** Clear all resolutions (e.g., when items change structurally) */
    clearResolutions: () => void;
}

/**
 * Classify an item's reconciliation status.
 * Items with a linked catalog ID are MATCHED.
 * Items with a manual resolution status keep that status.
 * Otherwise, items with a description but no catalog link are UNMATCHED.
 */
function classifyItem<T extends ReconcilableItem>(
    item: T,
    index: number,
    resolution?: ItemResolution
): ClassifiedItem<T> {
    // If a resolution exists for this item, use that status
    if (resolution) {
        return {
            item,
            index,
            status: resolution.status,
            justification: resolution.justification,
        };
    }

    // If the item already has a reconciliation status from prior processing, use it
    if (item.reconciliationStatus) {
        return {
            item,
            index,
            status: item.reconciliationStatus,
            justification: item.reconciliationJustification,
        };
    }

    // If linked to a catalog item, it's matched
    if (item.itemCatalogId) {
        return { item, index, status: 'MATCHED' };
    }

    // If description exists but no catalog link, it's unmatched
    if (item.description && item.description.trim().length > 0) {
        return { item, index, status: 'UNMATCHED' };
    }

    // Empty items are considered matched (no action needed)
    return { item, index, status: 'MATCHED' };
}

export function useCatalogItemReconciliation<T extends ReconcilableItem>(
    items: T[]
): UseReconciliationReturn<T> {
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [resolutions, setResolutions] = useState<Map<number, ItemResolution>>(new Map());

    const classifiedItems = useMemo(() => {
        return items.map((item, index) => classifyItem(item, index, resolutions.get(index)));
    }, [items, resolutions]);

    const unresolvedCount = useMemo(() => {
        return classifiedItems.filter(
            ci => ci.status === 'UNMATCHED' || ci.status === 'LOW_CONFIDENCE'
        ).length;
    }, [classifiedItems]);

    const hasUnresolved = unresolvedCount > 0;

    const openModal = useCallback(() => setIsModalOpen(true), []);
    const closeModal = useCallback(() => setIsModalOpen(false), []);

    const resolveItem = useCallback((resolution: ItemResolution) => {
        setResolutions(prev => {
            const next = new Map(prev);
            next.set(resolution.itemIndex, resolution);
            return next;
        });
    }, []);

    const resolveAll = useCallback((newResolutions: ItemResolution[]) => {
        setResolutions(prev => {
            const next = new Map(prev);
            newResolutions.forEach(r => next.set(r.itemIndex, r));
            return next;
        });
    }, []);

    const clearResolutions = useCallback(() => {
        setResolutions(new Map());
    }, []);

    return {
        classifiedItems,
        unresolvedCount,
        hasUnresolved,
        openModal,
        closeModal,
        isModalOpen,
        resolutions,
        resolveItem,
        resolveAll,
        clearResolutions,
    };
}
