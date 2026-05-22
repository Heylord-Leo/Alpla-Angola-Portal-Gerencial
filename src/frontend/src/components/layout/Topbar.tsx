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
        <header data-tour="topbar" style={{
            height: '64px',
            backgroundColor: 'var(--color-primary)',
            borderBottom: '1px solid rgba(0, 0, 0, 0.1)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0 3rem',
            position: 'sticky',
            top: 0,
            zIndex: Z_INDEX.TOPBAR as any, // Elevated for dropdowns
            color: 'var(--color-bg-surface)',
            fontFamily: 'var(--font-family-display)',
            boxShadow: 'var(--shadow-sm)'
        }}>
            {/* Left Zone: Minimal Context (Branding handled by Sidebar) */}
            <div style={{ display: 'flex', alignItems: 'center', width: '260px' }}>
                {/* Space reserved to balance the Sidebar width below */}
            </div>

            {/* Center Zone: Search Utility (Primary Element) */}
            <div data-tour="module-search" style={{ flex: 1, display: 'flex', justifyContent: 'center' }}>
                <GlobalSearch />
            </div>

            {/* Right Zone: Integrated Actions */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '2rem', justifyContent: 'flex-end', width: '320px' }}>
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

