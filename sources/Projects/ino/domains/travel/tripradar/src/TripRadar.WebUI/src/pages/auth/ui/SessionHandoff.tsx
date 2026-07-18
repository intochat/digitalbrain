import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { apiClient } from 'shared/api/interceptors';
import { ROUTES } from 'shared/config/routes';
import { useAuthStore } from 'shared/store/auth';
import { LoadingSpinner } from 'shared/ui';

export const SessionHandoff = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const initializeAuth = useAuthStore(state => state.initializeAuth);
  const [error, setError] = useState<string | null>(null);
  const ranRef = useRef(false);

  useEffect(() => {
    if (ranRef.current) return;
    ranRef.current = true;

    const params = new URLSearchParams(window.location.hash.slice(1));
    const accessToken = params.get('at');
    const refreshToken = params.get('rt');

    window.history.replaceState(null, '', window.location.pathname + window.location.search);

    const fail = (message: string) => {
      console.error('Session handoff failed:', message);
      setError(t('Session handoff failed. Please sign in again.'));
      setTimeout(() => navigate(ROUTES.LOGIN, { replace: true }), 3000);
    };

    if (!accessToken || !refreshToken) {
      fail('Missing tokens in URL fragment');
      return;
    }

    (async () => {
      try {
        await apiClient.post('/api/v1/tokens/refresh-tokens', { accessToken, refreshToken });
        await initializeAuth();
        navigate(ROUTES.PROFILE, { replace: true });
      } catch (err) {
        fail(err instanceof Error ? err.message : 'Unknown error');
      }
    })();
  }, [initializeAuth, navigate, t]);

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark">
        <div className="max-w-md w-full p-8">
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
            <p className="text-sm text-red-800 dark:text-red-200">{error}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark">
      <div className="flex flex-col items-center gap-4">
        <LoadingSpinner />
        <p className="text-content-secondary dark:text-content-secondary-dark">
          {t('Signing you in...')}
        </p>
      </div>
    </div>
  );
};
