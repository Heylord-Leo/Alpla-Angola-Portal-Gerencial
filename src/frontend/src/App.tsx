import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ErrorBoundary } from './components/ErrorBoundary';
import { AppShell } from './layouts/AppShell';
import { RequestsList } from './pages/Requests/RequestsList';
import { RequestCreate } from './pages/Requests/RequestCreate';
import { RequestEdit } from './pages/Requests/RequestEdit';
import { BuyerItemsList } from './pages/Buyer/BuyerItemsList';
import { ReceivingWorkspace } from './pages/Receiving/ReceivingWorkspace';
import ReceivingOperation from './pages/Receiving/ReceivingOperation';
import { MasterData } from './pages/Settings/MasterData';
import { DocumentExtractionSettings } from './pages/Settings/DocumentExtractionSettings';
import PurchasingLandingPage from './pages/Purchasing/PurchasingLandingPage';
import { ApprovalCenter } from './pages/Approvals/ApprovalCenter';

import { Dashboard } from './pages/Dashboard/Dashboard';
import { AdministratorWorkspace } from './pages/Admin/AdministratorWorkspace';
import { SystemLogs } from './pages/Admin/SystemLogs';
import { ServiceDiagnosis } from './pages/Admin/ServiceDiagnosis';
import { IntegrationHealth } from './pages/Admin/IntegrationHealth';
import UserManagement from './pages/Admin/UserManagement';
import { AuthProvider, useAuth } from './features/auth/AuthContext';
import LoginPage from './pages/LoginPage';
import ChangePasswordPage from './pages/ChangePasswordPage';
import { ROLES } from './constants/roles';

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

function AppContent() {
    return (
        <Routes>
            <Route path="/login" element={<LoginPage />} />
            
            <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
                <Route path="/change-password" element={<ChangePasswordPage />} />
                <Route path="/" element={<Navigate to="/requests" replace />} />
                <Route path="/dashboard" element={<Dashboard />} />
                <Route path="/approvals" element={<ApprovalCenter />} />
                <Route path="/purchasing" element={<PurchasingLandingPage />} />
                <Route path="/requests" element={<RequestsList />} />
                <Route path="/requests/new" element={<ErrorBoundary fallbackName="RequestCreate"><RequestCreate /></ErrorBoundary>} />
                <Route path="/requests/:id" element={<ErrorBoundary fallbackName="RequestEdit"><RequestEdit /></ErrorBoundary>} />
                <Route path="/requests/:id/edit" element={<ErrorBoundary fallbackName="RequestEdit"><RequestEdit /></ErrorBoundary>} />
                <Route path="/receiving/workspace" element={<ReceivingWorkspace />} />
                <Route path="/receiving/operation/:id" element={<ReceivingOperation />} />
                <Route path="/buyer/items" element={<BuyerItemsList />} />
                {/* Settings Routes */}
                <Route path="/settings/master-data" element={<AdminRoute><MasterData /></AdminRoute>} />
                <Route path="/settings/document-extraction" element={<AdminRoute><DocumentExtractionSettings /></AdminRoute>} />
                
                {/* Administrator Workspace Routes */}
                <Route path="/admin/workspace" element={<AdminRoute><AdministratorWorkspace /></AdminRoute>} />
                <Route path="/admin/logs" element={<AdminRoute><SystemLogs /></AdminRoute>} />
                <Route path="/admin/users" element={<AdminRoute allowedRoles={[ROLES.LOCAL_MANAGER]}><UserManagement /></AdminRoute>} />
                <Route path="/admin/diagnosis" element={<AdminRoute><ServiceDiagnosis /></AdminRoute>} />
                <Route path="/admin/health" element={<AdminRoute><IntegrationHealth /></AdminRoute>} />
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
