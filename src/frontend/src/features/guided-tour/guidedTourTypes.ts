import type { Step } from 'react-joyride';

/**
 * Guided Tour Types
 * 
 * Extensible type system for the guided tour feature.
 * Supports portal-level, module-level, and page-level tours
 * via a registry-based architecture (DEC-132).
 */

/** Unique identifiers for each tour in the system */
export type TourId =
    | 'portal-main'
    | 'module-purchasing-logistics'
    | 'page-requests'
    | 'page-buyer-items'
    | 'page-receiving-workspace'
    | 'page-approvals-center'
    | 'drawer-approval-area'
    | 'drawer-approval-final'
    | 'module-it-equipment';

/** Tour hierarchy level */
export type TourLevel = 'portal' | 'module' | 'page' | 'drawer';

/** Tour completion status persisted in localStorage */
export type TourStatus = 'completed' | 'skipped' | 'not-started';

/** Persisted state for a single tour */
export interface TourState {
    status: TourStatus;
    /** ISO 8601 timestamp of last interaction (completion/skip) */
    lastSeenAt: string | null;
}

/**
 * Tour step type — directly uses Joyride's Step type.
 * We keep a named alias for readability and future extension.
 */
export type TourStep = Step;

/**
 * TourDefinition — a single entry in the tour registry.
 * Describes a complete tour with its steps, applicable routes, and metadata.
 */
export interface TourDefinition {
    /** Unique tour identifier */
    id: TourId;
    /** Hierarchy level */
    level: TourLevel;
    /** User-facing label in Portuguese (shown in dropdown menu) */
    label: string;
    /** Route prefixes this tour applies to (matched with startsWith) */
    routes: string[];
    /** Step definitions for the tour */
    steps: TourStep[];
    /** If true, auto-show welcome modal on first access (only portal-main) */
    autoShow?: boolean;
    /**
     * CSS selector for the scroll container used by this tour.
     * When set, the scroll helper scrolls this container instead of the window.
     * Used for drawer/overlay tours where the content scrolls inside a panel.
     */
    scrollContainerSelector?: string;
}

/** Context value exposed by GuidedTourProvider */
export interface GuidedTourContextValue {
    /** Start a specific tour by ID. Defaults to 'portal-main' if omitted. */
    startTour: (tourId?: TourId) => void;
    /** Start the module-level tour for the current route (if any) */
    startCurrentModuleTour: () => void;
    /** Start the page-level tour for the current route (if any) */
    startCurrentPageTour: () => void;
    /** Get list of tours available for the current route */
    getAvailableTours: () => TourDefinition[];
    /** Currently active tour ID (null if no tour running) */
    activeTourId: TourId | null;
}
