import { ReactNode, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from 'shared/config/routes';
import { useAuthStore } from 'shared/store/auth';

interface AuthGuardProps {
  children: ReactNode;
  redirectTo?: string;
}

/**
 * AuthGuard - защищает страницы аутентификации от уже залогиненных пользователей
 * Используется для страниц логина, регистрации, сброса пароля
 */
export const AuthGuard = ({ children, redirectTo = ROUTES.PROFILE }: AuthGuardProps) => {
  const navigate = useNavigate();
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  const isLoading = useAuthStore(state => state.isLoading);

  useEffect(() => {
    if (isAuthenticated) {
      navigate(redirectTo, { replace: true });
    }
  }, [isAuthenticated, navigate, redirectTo]);

  if (isLoading || isAuthenticated) {
    return null;
  }

  return <>{children}</>;
};
