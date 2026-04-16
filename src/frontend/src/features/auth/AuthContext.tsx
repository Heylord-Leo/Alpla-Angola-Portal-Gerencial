import React, { createContext, useContext, useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROLES } from '../../constants/roles';

interface User {
  id: string;
  email: string;
  fullName: string;
  roles: string[];
  plants: string[];
  departments: string[];
  managedDepartmentIds: number[];
  mustChangePassword: boolean;
}

interface AuthContextType {
  user: User | null;
  token: string | null;
  login: (data: { token: string; user: User }) => void;
  logout: () => void;
  isAuthenticated: boolean;
  isAdmin: boolean;
  isLocalManager: boolean;
  isDepartmentManager: boolean;
  hasHRAccess: boolean;
  /** True if user can access the HR module (HR role, admin, or department manager). */
  hasHRModuleAccess: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    const storedToken = sessionStorage.getItem('auth_token');
    const storedUser = sessionStorage.getItem('auth_user');
    if (storedToken && storedUser) {
      setToken(storedToken);
      setUser(JSON.parse(storedUser));
    }
    setIsLoading(false);
  }, []);

  const login = (data: { token: string; user: User }) => {
    sessionStorage.setItem('auth_token', data.token);
    sessionStorage.setItem('auth_user', JSON.stringify(data.user));
    setToken(data.token);
    setUser(data.user);
    
    if (data.user.mustChangePassword) {
      navigate('/change-password');
    } else {
      navigate('/');
    }
  };

  const logout = () => {
    sessionStorage.removeItem('auth_token');
    sessionStorage.removeItem('auth_user');
    setToken(null);
    setUser(null);
    navigate('/login');
  };

  const isAdmin = user?.roles.includes(ROLES.SYSTEM_ADMINISTRATOR) || false;
  const isLocalManager = user?.roles.includes(ROLES.LOCAL_MANAGER) || false;
  const isDepartmentManager = (user?.managedDepartmentIds?.length ?? 0) > 0;
  const hasHRAccess = user?.roles.includes(ROLES.HR) || isAdmin;
  // HR Module access: HR role users, admins, or department managers (ResponsibleUser of any department)
  const hasHRModuleAccess = hasHRAccess || isDepartmentManager;

  if (isLoading) {
    return null; // Or a loading spinner
  }

  return (
    <AuthContext.Provider value={{ 
      user, 
      token, 
      login, 
      logout, 
      isAuthenticated: !!token,
      isAdmin,
      isLocalManager,
      isDepartmentManager,
      hasHRAccess,
      hasHRModuleAccess
    }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
};
