/**
 * Centralized Z-Index Constants
 * Briding CSS variables for type-safe usage in inline styles.
 * 
 * Hierarchy:
 * Dropdown (500) < Popover (1000) < Sticky (1100) < Topbar (1200) < Sidebar (1250) < Drawer (1400) < Modal (1500) < Toast (1600) < Tooltip (1700)
 */
export const Z_INDEX = {
  /** Default content level */
  BASE: 'var(--z-base)',
  /** Kebab menus, filter lists, normal selects */
  DROPDOWN: 'var(--z-dropdown)',
  /** Notification panels, DatePickers, context menus */
  POPOVER: 'var(--z-popover)',
  /** Page action headers (RequestActionHeader) */
  STICKY: 'var(--z-sticky)',
  /** Global App Shell Topbar */
  TOPBAR: 'var(--z-topbar)',
  /** Sidebar toggle button (must be clickable over Topbar) */
  SIDEBAR: 'var(--z-sidebar)',
  /** Slide-out panels (ApprovalCenter, UserProfile, Help) */
  DRAWER: 'var(--z-drawer)',
  /** Confirmation dialogs, Detail modals (ABOVE drawers) */
  MODAL: 'var(--z-modal)',
  /** Feedback messages / toast notifications */
  TOAST: 'var(--z-toast)',
  /** Contextual hints (always top-most) */
  TOOLTIP: 'var(--z-tooltip)'
} as const;
