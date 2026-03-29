import { GlobalSearch } from './GlobalSearch';
import { UserDropdown } from './UserDropdown';
import { NotificationBell } from './NotificationBell';

/**
 * Topbar Redesign (Shell 2.0)
 * 1. Left: Removed "Portal Gerencial" text. Focus on clean spacing.
 * 2. Center: Global Search (Navigation scope V1)
 * 3. Right: Notifications + User Account Dropdown
 */
export function Topbar() {
    return (
        <header style={{
            height: '64px',
            backgroundColor: 'var(--color-primary)',
            borderBottom: '4px solid var(--color-accent)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0 3rem',
            position: 'sticky',
            top: 0,
            zIndex: 100, // Elevated for dropdowns
            color: 'var(--color-bg-surface)',
            fontFamily: 'var(--font-family-display)'
        }}>
            {/* Left Zone: Minimal Context (Branding handled by Sidebar) */}
            <div style={{ display: 'flex', alignItems: 'center', width: '260px' }}>
                {/* Space reserved to balance the Sidebar width below */}
            </div>

            {/* Center Zone: Search Utility (Primary Element) */}
            <div style={{ flex: 1, display: 'flex', justifyContent: 'center' }}>
                <GlobalSearch />
            </div>

            {/* Right Zone: Integrated Actions */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '2rem', justifyContent: 'flex-end', width: '320px' }}>
                <NotificationBell />
                <div style={{ 
                    borderLeft: '1px solid rgba(255, 255, 255, 0.2)', 
                    height: '32px',
                    marginLeft: '-0.5rem'
                }} />
                <UserDropdown />
            </div>
        </header>
    );
}

