import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useSyncTelegramUsernameMutation } from 'entities/auth';
import { convertTelegramDataToApiFormat, validateTelegramData } from 'features/auth/lib/telegram';
import { handleTelegramAuthSuccess } from 'features/auth/lib/telegramAuthHelper';
import { ErrorAlert } from 'features/auth/ui/ErrorAlert';
import { TelegramConnect } from 'features/auth/ui/TelegramConnect';
import type { LinkTelegramResponse, TelegramData } from 'shared/api/types';
import { ROUTES } from 'shared/config/routes';
import { useAuthStore } from 'shared/store/auth';
import { LoadingSpinner } from 'shared/ui';

export const TelegramUsernameSync = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const login = useAuthStore(state => state.login);
  const { mutate: syncTelegramUsername, isPending: isSyncPending } = useSyncTelegramUsernameMutation();

  const [error, setError] = useState<string | null>(null);
  const [isProcessingRedirect, setIsProcessingRedirect] = useState(false);
  const hasProcessedRedirect = useRef(false);

  const hasTelegramCallbackParams = useMemo(
    () => ['id', 'first_name', 'auth_date', 'hash'].every(param => searchParams.has(param)),
    [searchParams]
  );

  const applyAuthAndRedirect = useCallback(
    (response: LinkTelegramResponse) => {
      const error = handleTelegramAuthSuccess({
        response,
        login,
        navigate,
        targetRoute: ROUTES.PROFILE,
      });
      if (error) {
        setError(t(error));
      }
    },
    [login, navigate, t]
  );

  useEffect(() => {
    if (!hasTelegramCallbackParams || hasProcessedRedirect.current) {
      return;
    }

    hasProcessedRedirect.current = true;

    const telegramData: Partial<TelegramData> = {
      id: searchParams.get('id') ? Number(searchParams.get('id')) : undefined,
      first_name: searchParams.get('first_name') || undefined,
      last_name: searchParams.get('last_name') || undefined,
      username: searchParams.get('username') || undefined,
      photo_url: searchParams.get('photo_url') || undefined,
      auth_date: searchParams.get('auth_date') ? Number(searchParams.get('auth_date')) : undefined,
      hash: searchParams.get('hash') || undefined,
    };

    if (!validateTelegramData(telegramData)) {
      setError(t('Invalid Telegram callback data. Please try again.'));
      return;
    }

    setIsProcessingRedirect(true);
    syncTelegramUsername(
      {
        telegramAuth: convertTelegramDataToApiFormat(telegramData as TelegramData),
      },
      {
        onSuccess: response => {
          setIsProcessingRedirect(false);
          applyAuthAndRedirect(response);
        },
        onError: syncError => {
          setIsProcessingRedirect(false);
          const errorMessage =
            syncError instanceof Error ? syncError.message : t('Failed to sync Telegram username. Please try again.');
          setError(errorMessage);
        },
      }
    );
  }, [hasTelegramCallbackParams, searchParams, syncTelegramUsername, t, applyAuthAndRedirect]);

  if (isProcessingRedirect) {
    return (
      <div className="flex-1 flex items-center justify-center bg-surface dark:bg-surface-dark">
        <div className="flex flex-col items-center gap-4">
          <LoadingSpinner />
          <p className="text-content-secondary dark:text-content-secondary-dark">{t('Syncing Telegram username...')}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 bg-surface dark:bg-surface-dark px-4 py-8">
      <div className="w-full max-w-lg mx-auto space-y-5">
        <div className="text-center space-y-2">
          <h1 className="text-2xl font-semibold text-content dark:text-content-dark">{t('Sync Telegram Username')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Confirm your Telegram account to update your username mapping.')}
          </p>
        </div>

        {error && (
          <ErrorAlert
            title={t('Telegram Sync Failed')}
            message={error}
            severity="error"
            actions={[
              {
                label: t('Retry'),
                onClick: () => {
                  setError(null);
                  hasProcessedRedirect.current = false;
                },
                variant: 'secondary',
              },
            ]}
            onDismiss={() => setError(null)}
          />
        )}

        {!hasTelegramCallbackParams && (
          <div className="bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-xl p-5">
            <TelegramConnect
              mode="usernameSync"
              onSuccess={response => {
                setError(null);
                applyAuthAndRedirect(response);
              }}
              onError={syncError => setError(syncError)}
            />
          </div>
        )}

        {isSyncPending && (
          <div className="flex items-center justify-center gap-2 text-sm text-content-secondary dark:text-content-secondary-dark">
            <LoadingSpinner />
            <span>{t('Syncing username...')}</span>
          </div>
        )}
      </div>
    </div>
  );
};
