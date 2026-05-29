import type React from 'react';

/**
 * Live Guide Type Definitions
 *
 * Extensible type system for the Live Guide feature — an interactive
 * step-by-step guidance system that helps users complete real tasks
 * (e.g., filling forms) while validating each step before progressing.
 *
 * This is distinct from the standard Guided Tour (explanatory) system.
 * Live Guides use `data-guide` attributes; tours use `data-tour`.
 */

/** Unique identifiers for each live guide in the system. Extend as new guides are added. */
export type LiveGuideId = 'request-creation-live-guide' | 'quotation-management-live-guide';

/** The kind of user interaction required to satisfy a step */
export type RequiredAction = 'input' | 'select' | 'upload' | 'click' | 'none';

/** Completion status persisted in localStorage */
export type LiveGuideStatus = 'completed' | 'cancelled' | 'not-started';

/** Persisted state for a single live guide */
export interface LiveGuideState {
    status: LiveGuideStatus;
    /** ISO 8601 timestamp of last interaction */
    lastSeenAt: string | null;
}

/**
 * LiveGuideStep — a single step in a live guide.
 *
 * Each step targets a DOM element via a CSS selector (typically `[data-guide="..."]`),
 * displays guidance text, and optionally validates that the user has completed
 * the required action before allowing progression to the next step.
 */
export interface LiveGuideStep {
    /** Unique step identifier within the guide */
    id: string;
    /** CSS selector for the target element (e.g., `[data-guide="request-title"]`) */
    target: string;
    /** User-facing title (Portuguese) */
    title: string;
    /** User-facing body content (Portuguese). Accepts plain string or React nodes for rich formatting. */
    content: string | React.ReactNode;
    /** Tooltip placement relative to the target */
    placement?: 'top' | 'bottom' | 'left' | 'right' | 'auto' | 'center';
    /** The kind of user action required at this step */
    requiredAction: RequiredAction;
    /** Returns true when the step's required action is satisfied */
    validate?: () => boolean;
    /** Message shown when the user tries to proceed but validation fails (Portuguese) */
    validationMessage?: string;
    /** If true, the user may skip this step without completing the action */
    allowSkip: boolean;
    /** If provided and returns false, this step is auto-skipped */
    condition?: () => boolean;
    /** Fallback text shown when the target element is not found in the DOM */
    fallbackContent?: string;
    /** Called before this step becomes active */
    beforeStep?: () => void;
    /** Called after the user leaves this step (next/skip) */
    afterStep?: () => void;
}

/**
 * LiveGuideDefinition — a complete live guide registered in the system.
 *
 * Describes the guide's metadata, applicability, and step sequence.
 */
export interface LiveGuideDefinition {
    /** Unique guide identifier */
    id: LiveGuideId;
    /** Always 'live-guide' — distinguishes from standard tours */
    type: 'live-guide';
    /** Functional module this guide belongs to (e.g., 'requests') */
    module: string;
    /** Route where this guide is applicable (matched with startsWith) */
    route: string;
    /** User-facing title (Portuguese) */
    title: string;
    /** User-facing description (Portuguese) */
    description: string;
    /** Semantic version of this guide definition */
    version: string;
    /** Whether this guide is currently active */
    enabled: boolean;
    /** Ordered step definitions */
    steps: LiveGuideStep[];
}

/** Context value exposed by LiveGuideProvider */
export interface LiveGuideContextValue {
    /** Start a live guide by ID */
    startLiveGuide: (guideId: LiveGuideId) => void;
    /** Close the currently active live guide */
    closeLiveGuide: () => void;
    /** Whether any live guide is currently running */
    isLiveGuideActive: boolean;
    /** The ID of the currently active guide (null if none) */
    activeLiveGuideId: LiveGuideId | null;
}
