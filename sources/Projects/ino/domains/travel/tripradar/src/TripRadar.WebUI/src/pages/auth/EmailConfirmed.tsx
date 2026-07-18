import { useEffect, useState } from 'react';
import { CheckCircle } from 'lucide-react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { handleTelegramAuthSuccess } from 'features/auth/lib/telegramAuthHelper';
import { ErrorAlert } from 'features/auth/ui/ErrorAlert';
import { TelegramConnect } from 'features/auth/ui/TelegramConnect';
import { ROUTES } from 'shared/config/routes';
import { getEmailFromUrlParams } from 'shared/lib';
import { useAuthStore } from 'shared/store/auth';
import { Button } from 'shared/ui';

export const EmailConfirmed = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { login } = useAuthStore();
  const [email, setEmail] = useState<string | null>(null);
  const [telegramError, setTelegramError] = useState<string>('');

  useEffect(() => {
    const emailParam = getEmailFromUrlParams(searchParams);
    if (emailParam) {
      setEmail(emailParam);
      return;
    }

    const storedEmail = sessionStorage.getItem('registration_email');
    if (storedEmail) {
      setEmail(storedEmail);
      return;
    }

    setEmail(null);
  }, [searchParams]);

  return (
    <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
      <div className="w-full max-w-[380px] mx-auto space-y-6">
        <div className="text-center space-y-3">
          <div className="mx-auto w-12 h-12 bg-emerald-50 dark:bg-emerald-500/10 rounded-full flex items-center justify-center">
            <CheckCircle className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
          </div>
          <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Email Confirmed')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark leading-relaxed">
            {t('Connect your Telegram to complete registration')}
          </p>
        </div>

        {email ? (
          <div className="space-y-4">
            <TelegramConnect
              email={email}
              showRequirementsInfo={false}
              onSuccess={response => {
                const error = handleTelegramAuthSuccess({
                  response,
                  login,
                  navigate,
                  targetRoute: ROUTES.PROFILE,
                });
                if (error) setTelegramError(t(error));
              }}
              onError={error => {
                console.error('Telegram linking error:', error);
                setTelegramError(error);
              }}
            />

            {telegramError && (
              <ErrorAlert
                title={t('Telegram Connection Failed')}
                message={telegramError}
                severity="error"
                actions={[
                  {
                    label: t('Try again'),
                    onClick: () => setTelegramError(''),
                    variant: 'secondary',
                  },
                ]}
                onDismiss={() => setTelegramError('')}
              />
            )}
          </div>
        ) : (
          <div className="space-y-4 text-center">
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
              {t('No registration data found')}
            </p>
            <Button onClick={() => navigate(ROUTES.LOGIN)} className="w-full">
              {t('Return to Login')}
            </Button>
          </div>
        )}
      </div>
    </div>
  );
};
