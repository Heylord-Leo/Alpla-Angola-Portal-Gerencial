/**
 * guidedTourSteps.ts — Backward Compatibility Barrel
 * 
 * Steps and filtering logic have been moved to the tours/ subdirectory
 * as part of the multi-tour registry architecture (DEC-132).
 * This file re-exports them to avoid breaking existing imports.
 */
export { PORTAL_MAIN_STEPS, filterActiveSteps } from './tours/portalMainTour';
