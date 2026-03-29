/**
 * TEMPORARY DEVELOPMENT SCAFFOLD
 * 
 * This file provides a temporary mechanism for detecting administrative roles 
 * during the initial development of the Administrator Workspace.
 * 
 * DESIGN NOTE: This must be replaced by a real JWT-based Role-Based Access Control (RBAC) 
 * system integrated with the backend once the enterprise authentication layer is implemented.
 */

/**
 * Temporary development scaffold for Administrator role detection.
 * To be replaced by JWT-based RBAC in future steps.
 */
export function isAdmin(): boolean {
    // Check for explicit development scaffold flag
    return localStorage.getItem('dev_admin_scaffold') === 'true';
}

/**
 * Helper to enable/disable admin scaffold in the console.
 * Usage: window.enableAdminScaffold(true)
 */
if (typeof window !== 'undefined') {
    (window as any).enableAdminScaffold = (enabled: boolean) => {
        localStorage.setItem('dev_admin_scaffold', enabled ? 'true' : 'false');
        console.log(`[AdminScaffold] ${enabled ? 'Enabled' : 'Disabled'}. Reloading...`);
        window.location.reload();
    };
}
