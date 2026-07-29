import { Z_INDEX } from '../../constants/ui';
import { GlobalSearch } from './GlobalSearch';
import { UserDropdown } from './UserDropdown';
import { NotificationBell } from './NotificationBell';
import { GuidedTourButton } from '../../features/guided-tour/GuidedTourButton';


/**
 * Topbar Redesign (Shell 2.0)
 * 1. Left: Removed "Portal Gerencial" text. Focus on clean spacing.
 * 2. Center: Global Search (Navigation scope V1)
 * 3. Right: Help + Notifications + User Account Dropdown
 */
export function Topbar() {

    return (
        <header data-tour="topbar" className="app-shell-topbar" style={{
            height: '64px',
            backgroundColor: 'var(--color-primary)',
            borderBottom: '1px solid rgba(0, 0, 0, 0.1)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0 var(--spacing-shell-x, 3rem)',
            position: 'sticky',
            top: 'var(--env-banner-offset, 0px)',
            zIndex: Z_INDEX.TOPBAR as any, // Elevated for dropdowns
            color: 'var(--color-bg-surface)',
            fontFamily: 'var(--font-family-display)',
            boxShadow: 'var(--shadow-sm)'
        }}>
            {/* Left Zone: Minimal Context (Branding handled by Sidebar) */}
            <div style={{ display: 'flex', alignItems: 'center', width: 'auto', minWidth: '80px', flex: '0 1 260px' }}>
                {/* Space reserved to balance the Sidebar width below */}
            </div>

            {/* Center Zone: Search Utility (Primary Element) */}
            <div data-tour="module-search" style={{ flex: 1, display: 'flex', justifyContent: 'center', minWidth: 0, overflow: 'hidden' }}>
                <GlobalSearch />
            </div>

            {/* Right Zone: Integrated Actions */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', justifyContent: 'flex-end', width: 'auto', minWidth: '180px', flex: '0 1 320px' }}>
                <GuidedTourButton />
                <div data-tour="notifications">
                    <NotificationBell />
                </div>
                <div style={{ 
                    borderLeft: '1px solid rgba(255, 255, 255, 0.2)', 
                    height: '32px',
                    marginLeft: '-0.5rem'
                }} />
                <div data-tour="user-profile">
                    <UserDropdown />
                </div>
            </div>
        </header>
    );
}

