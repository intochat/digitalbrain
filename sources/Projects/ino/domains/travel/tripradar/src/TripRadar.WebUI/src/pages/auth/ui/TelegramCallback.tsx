import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useLinkTelegramMutation } from 'entities/auth';
import { convertTelegramDataToApiFormat, validateTelegramData } from 'features/auth/lib/telegram';
import type { TelegramData } from 'shared/api/types';
import { ROUTES } from 'shared/config/routes';
import { LoadingSpinner } from 'shared/ui';

export const TelegramCallback = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const { mutate: linkTelegram } = useLinkTelegramMutation();

  useEffect(() => {
    const processTelegramCallback = () => {
      // Extract Telegram data from URL query parameters
      const telegramData: Partial<TelegramData> = {
        id: searchParams.get('id') ? Number(searchParams.get('id')) : undefined,
        first_name: searchParams.get('first_name') || undefined,
        last_name: searchParams.get('last_name') || undefined,
        username: searchParams.get('username') || undefined,
        photo_url: searchParams.get('photo_url') || undefined,
        auth_date: searchParams.get('auth_date') ? Number(searchParams.get('auth_date')) : undefined,
        hash: searchParams.get('hash') || undefined,
      };

      // Validate telegram data
      if (!validateTelegramData(telegramData)) {
        console.error('❌ Invalid Telegram data from callback:', telegramData);
        setError(t('Invalid data received from Telegram. Please try again.'));
        return;
      }

      // Get email from sessionStorage (stored before redirect)
      const email = sessionStorage.getItem('telegram_auth_email');
      if (!email) {
        console.error('❌ Email not found in session storage');
        setError(t('Session expired. Please try logging in again.'));
        setTimeout(() => navigate(ROUTES.LOGIN), 2000);
        return;
      }

      // Call link Telegram API
      linkTelegram(
        {
          email,
          telegramAuth: convertTelegramDataToApiFormat(telegramData as TelegramData),
        },
        {
          onSuccess: () => {
            // Cookies are set by the backend automatically
            sessionStorage.removeItem('telegram_auth_email');
            navigate(ROUTES.PROFILE);
          },
          onError: error => {
            console.error('❌ Telegram linking failed:', error);

            // Extract error message
            const errorMessage =
              error instanceof Error ? error.message : t('Failed to link Telegram account. Please try again.');

            setError(errorMessage);

            // Redirect back to login after showing error
            setTimeout(() => navigate(ROUTES.LOGIN), 3000);
          },
        }
      );
    };

    processTelegramCallback();
  }, [linkTelegram, navigate, searchParams, t]);

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-surface dark:bg-surface-dark">
        <div className="max-w-md w-full p-8">
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
            <p className="text-sm text-red-800 dark:text-red-200">{error}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex items-center justify-center bg-surface dark:bg-surface-dark">
      <div className="flex flex-col items-center gap-4">
        <LoadingSpinner />
        <p className="text-content-secondary dark:text-content-secondary-dark">
          {t('Connecting your Telegram account...')}
        </p>
      </div>
    </div>
  );
};
