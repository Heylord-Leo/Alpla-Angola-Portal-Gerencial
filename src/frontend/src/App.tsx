import React, { Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { AppShell } from './layouts/AppShell';
import { LoadingSkeleton } from './components/ui/LoadingSkeleton';
import { AuthProvider, useAuth } from './features/auth/AuthContext';
import { ROLES } from './constants/roles';

// ─── Eagerly loaded (critical path / small) ───
import LoginPage from './pages/LoginPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import ChangePasswordPage from './pages/ChangePasswordPage';
import { Dashboard } from './pages/Dashboard/Dashboard';

// ─── Lazily loaded (heavy / isolated modules) ───
const RequestsDashboard = React.lazy(() =>
    import('./pages/Requests/components/modern/RequestsDashboard').then(m => ({ default: m.RequestsDashboard }))
);
const RequestCreate = React.lazy(() =>
    import('./pages/Requests/RequestCreate').then(m => ({ default: m.RequestCreate }))
);
const RequestEdit = React.lazy(() =>
    import('./pages/Requests/RequestEdit').then(m => ({ default: m.RequestEdit }))
);
const ApprovalCenter = React.lazy(() =>
    import('./pages/Approvals/ApprovalCenter').then(m => ({ default: m.ApprovalCenter }))
);
const BuyerItemsList = React.lazy(() =>
    import('./pages/Buyer/BuyerItemsList').then(m => ({ default: m.BuyerItemsList }))
);
const PurchasingLandingPage = React.lazy(() =>
    import('./pages/Purchasing/PurchasingLandingPage')
);
const ReceivingWorkspace = React.lazy(() =>
    import('./pages/Receiving/ReceivingWorkspace').then(m => ({ default: m.ReceivingWorkspace }))
);
const ReceivingOperation = React.lazy(() =>
    import('./pages/Receiving/ReceivingOperation')
);
const FinanceLandingPage = React.lazy(() =>
    import('./pages/Finance/FinanceLandingPage')
);
const FinanceOverview = React.lazy(() =>
    import('./pages/Finance/FinanceOverview')
);
const FinancePaymentsList = React.lazy(() =>
    import('./pages/Finance/FinancePaymentsList')
);
const FinanceHistory = React.lazy(() =>
    import('./pages/Finance/FinanceHistory')
);
const FinanceBudgetConfig = React.lazy(() =>
    import('./pages/Finance/FinanceBudgetConfig')
);
const FinanceBudget = React.lazy(() =>
    import('./pages/Finance/FinanceBudget')
);

// Contracts pages
const ContractsLandingPage = React.lazy(() =>
    import('./pages/Contracts/ContractsLandingPage')
);
const ContractsList = React.lazy(() =>
    import('./pages/Contracts/ContractsList')
);
const ContractsAlerts = React.lazy(() =>
    import('./pages/Contracts/ContractsAlerts')
);
const ContractCreate = React.lazy(() =>
    import('./pages/Contracts/ContractCreate')
);
const ContractDetail = React.lazy(() =>
    import('./pages/Contracts/ContractDetail')
);

// HR pages
const HRLandingPage = React.lazy(() =>
    import('./pages/HR/HRLandingPage')
);
const HROverview = React.lazy(() =>
    import('./pages/HR/HROverview')
);
const HRLeaveList = React.lazy(() =>
    import('./pages/HR/HRLeaveList')
);
const HRTeamCalendar = React.lazy(() =>
    import('./pages/HR/HRTeamCalendar')
);
const EmployeeWorkspace = React.lazy(() =>
    import('./pages/HR/EmployeeWorkspace')
);
const HRBadgesLandingPage = React.lazy(() =>
    import('./pages/HR/HRBadgesLandingPage')
);
const BadgeLayoutDesigner = React.lazy(() =>
    import('./pages/HR/BadgeLayoutDesigner')
);
const BadgePrintHistoryPage = React.lazy(() =>
    import('./pages/HR/BadgePrintHistoryPage')
);

// Admin pages (isolated, rarely visited)
const MasterData = React.lazy(() =>
    import('./pages/Settings/MasterData').then(m => ({ default: m.MasterData }))
);
const DocumentExtractionSettings = React.lazy(() =>
    import('./pages/Settings/DocumentExtractionSettings').then(m => ({ default: m.DocumentExtractionSettings }))
);
const AdministratorWorkspace = React.lazy(() =>
    import('./pages/Admin/AdministratorWorkspace').then(m => ({ default: m.AdministratorWorkspace }))
);
const SystemLogs = React.lazy(() =>
    import('./pages/Admin/SystemLogs').then(m => ({ default: m.SystemLogs }))
);
const ServiceDiagnosis = React.lazy(() =>
    import('./pages/Admin/ServiceDiagnosis').then(m => ({ default: m.ServiceDiagnosis }))
);
const IntegrationHealth = React.lazy(() =>
    import('./pages/Admin/IntegrationHealth').then(m => ({ default: m.IntegrationHealth }))
);
const UserManagement = React.lazy(() =>
    import('./pages/Admin/UserManagement')
);
const SyncWorkspace = React.lazy(() =>
    import('./pages/Settings/SyncWorkspace').then(m => ({ default: m.SyncWorkspace }))
);
const HREmployeeDirectory = React.lazy(() =>
    import('./pages/HR/HREmployeeDirectory')
);

function ProtectedRoute({ children }: { children: React.ReactNode }) {
    const { isAuthenticated, user } = useAuth();
    
    if (!isAuthenticated) {
        return <Navigate to="/login" replace />;
    }

    if (user?.mustChangePassword && window.location.pathname !== '/change-password') {
        return <Navigate to="/change-password" replace />;
    }

    return <>{children}</>;
}

function AdminRoute({ children, allowedRoles }: { children: React.ReactNode, allowedRoles?: string[] }) {
    const { user, isAuthenticated, isAdmin } = useAuth();
    if (!isAuthenticated) return <Navigate to="/login" replace />;
    
    // If explicit roles are provided, check them
    if (allowedRoles) {
        const hasRole = user?.roles.some(role => allowedRoles.includes(role));
        if (!hasRole && !isAdmin) return <Navigate to="/dashboard" replace />;
    } else {
        // Fallback to strict Admin
        if (!isAdmin) return <Navigate to="/dashboard" replace />;
    }
    
    return <>{children}</>;
}

/** HR Module route guard — allows HR role, System Administrator, or Department Managers. */
function HRRoute({ children }: { children: React.ReactNode }) {
    const { isAuthenticated, hasHRModuleAccess } = useAuth();
    if (!isAuthenticated) return <Navigate to="/login" replace />;
    if (!hasHRModuleAccess) return <Navigate to="/dashboard" replace />;
    return <>{children}</>;
}

function AppContent() {
    return (
        <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            
            <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
                <Route path="/change-password" element={<ChangePasswordPage />} />
                <Route path="/" element={<Navigate to="/requests" replace />} />
                <Route path="/dashboard" element={<Dashboard />} />
                <Route path="/approvals" element={<Suspense fallback={<LoadingSkeleton />}><ApprovalCenter /></Suspense>} />
                <Route path="/purchasing" element={<Suspense fallback={<LoadingSkeleton />}><PurchasingLandingPage /></Suspense>} />
                <Route path="/requests" element={<Suspense fallback={<LoadingSkeleton />}><RequestsDashboard /></Suspense>} />
                <Route path="/requests/new" element={<Suspense fallback={<LoadingSkeleton />}><ErrorBoundary fallbackName="RequestCreate"><RequestCreate /></ErrorBoundary></Suspense>} />
                <Route path="/requests/:id" element={<Suspense fallback={<LoadingSkeleton />}><ErrorBoundary fallbackName="RequestEdit"><RequestEdit /></ErrorBoundary></Suspense>} />
                <Route path="/requests/:id/edit" element={<Suspense fallback={<LoadingSkeleton />}><ErrorBoundary fallbackName="RequestEdit"><RequestEdit /></ErrorBoundary></Suspense>} />
                <Route path="/receiving/workspace" element={<Suspense fallback={<LoadingSkeleton />}><ReceivingWorkspace /></Suspense>} />
                <Route path="/receiving/operation/:id" element={<Suspense fallback={<LoadingSkeleton />}><ReceivingOperation /></Suspense>} />
                <Route path="/buyer/items" element={<Suspense fallback={<LoadingSkeleton />}><BuyerItemsList /></Suspense>} />

                {/* Finance Workspace */}
                <Route path="/finance" element={<Suspense fallback={<LoadingSkeleton />}><FinanceLandingPage /></Suspense>}>
                    <Route index element={<Navigate to="overview" replace />} />
                    <Route path="overview" element={<Suspense fallback={<LoadingSkeleton />}><FinanceOverview /></Suspense>} />
                    <Route path="payments" element={<Suspense fallback={<LoadingSkeleton />}><FinancePaymentsList /></Suspense>} />
                    <Route path="history" element={<Suspense fallback={<LoadingSkeleton />}><FinanceHistory /></Suspense>} />
                    <Route path="budget-config" element={<Suspense fallback={<LoadingSkeleton />}><FinanceBudgetConfig /></Suspense>} />
                    <Route path="budget" element={<Suspense fallback={<LoadingSkeleton />}><FinanceBudget /></Suspense>} />
                </Route>

                {/* Contracts Workspace */}
                <Route path="/contracts" element={<Suspense fallback={<LoadingSkeleton />}><ContractsLandingPage /></Suspense>}>
                    <Route index element={<Navigate to="list" replace />} />
                    <Route path="list" element={<Suspense fallback={<LoadingSkeleton />}><ContractsList /></Suspense>} />
                    <Route path="alerts" element={<Suspense fallback={<LoadingSkeleton />}><ContractsAlerts /></Suspense>} />
                </Route>
                <Route path="/contracts/new" element={<Suspense fallback={<LoadingSkeleton />}><ContractCreate /></Suspense>} />
                <Route path="/contracts/:id/edit" element={<Suspense fallback={<LoadingSkeleton />}><ContractCreate /></Suspense>} />
                <Route path="/contracts/:id" element={<Suspense fallback={<LoadingSkeleton />}><ContractDetail /></Suspense>} />

                {/* HR Workspace */}
                <Route path="/hr" element={<HRRoute><Suspense fallback={<LoadingSkeleton />}><HRLandingPage /></Suspense></HRRoute>}>
                    <Route index element={<Navigate to="overview" replace />} />
                    <Route path="overview" element={<Suspense fallback={<LoadingSkeleton />}><HROverview /></Suspense>} />
                    <Route path="leave" element={<Suspense fallback={<LoadingSkeleton />}><HRLeaveList /></Suspense>} />
                    <Route path="calendar" element={<Suspense fallback={<LoadingSkeleton />}><HRTeamCalendar /></Suspense>} />
                    <Route path="directory" element={<Suspense fallback={<LoadingSkeleton />}><HREmployeeDirectory /></Suspense>} />
                    
                    {/* Compatibility Redirects */}
                    <Route path="employees" element={<Navigate to="badges/employees" replace />} />
                    <Route path="team-calendar" element={<Navigate to="calendar" replace />} />

                    <Route path="badges" element={<Suspense fallback={<LoadingSkeleton />}><HRBadgesLandingPage /></Suspense>}>
                        <Route index element={<Navigate to="employees" replace />} />
                        <Route path="employees" element={<Suspense fallback={<LoadingSkeleton />}><EmployeeWorkspace /></Suspense>} />
                        <Route path="layouts" element={<Suspense fallback={<LoadingSkeleton />}><BadgeLayoutDesigner /></Suspense>} />
                        <Route path="history" element={<Suspense fallback={<LoadingSkeleton />}><BadgePrintHistoryPage /></Suspense>} />
                    </Route>
                </Route>

                {/* Settings Routes */}
                <Route path="/settings/master-data" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><MasterData /></Suspense></AdminRoute>} />
                <Route path="/settings/sync/:entityType" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><SyncWorkspace /></Suspense></AdminRoute>} />
                <Route path="/settings/document-extraction" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><DocumentExtractionSettings /></Suspense></AdminRoute>} />
                
                {/* Administrator Workspace Routes */}
                <Route path="/admin/workspace" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><AdministratorWorkspace /></Suspense></AdminRoute>} />
                <Route path="/admin/logs" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><SystemLogs /></Suspense></AdminRoute>} />
                <Route path="/admin/users" element={<AdminRoute allowedRoles={[ROLES.LOCAL_MANAGER]}><Suspense fallback={<LoadingSkeleton />}><UserManagement /></Suspense></AdminRoute>} />
                <Route path="/admin/diagnosis" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><ServiceDiagnosis /></Suspense></AdminRoute>} />
                <Route path="/admin/health" element={<AdminRoute><Suspense fallback={<LoadingSkeleton />}><IntegrationHealth /></Suspense></AdminRoute>} />
            </Route>
        </Routes>
    );
}

export default function App() {
    return (
        <BrowserRouter>
            <AuthProvider>
                <AppContent />
            </AuthProvider>
        </BrowserRouter>
    );
}
