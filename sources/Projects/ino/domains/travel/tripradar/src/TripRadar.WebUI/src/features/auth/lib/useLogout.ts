import { useNavigate } from 'react-router-dom';
import { useLogoutMutation } from 'entities/auth/api';
import { useAuthStore } from 'shared/store/auth';

export const useLogout = () => {
  const { logout } = useAuthStore();
  const navigate = useNavigate();
  const logoutMutation = useLogoutMutation();

  const handleLogout = async () => {
    try {
      await logoutMutation.mutateAsync();
      logout();
      navigate('/', { replace: true });
    } catch (error) {
      console.error('Logout failed:', error);
    }
  };

  return handleLogout;
};
