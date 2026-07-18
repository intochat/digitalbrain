import { useEffect, useMemo, useState } from 'react';
import { CheckCircle, Loader2, MailWarning } from 'lucide-react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { apiClient } from 'shared/api';
import { ROUTES } from 'shared/config/routes';

type ConfirmationState = 'loading' | 'success' | 'error' | 'invalid';

export const ConfirmEmail = () => {
  const { t } = useFrontendLanguage();
  const location = useLocation();
  const navigate = useNavigate();
  const [state, setState] = useState<ConfirmationState>('loading');

  const { email, token } = useMemo(() => {
    const hashParams = new URLSearchParams(location.hash.startsWith('#') ? location.hash.slice(1) : location.hash);
    return {
      email: hashParams.get('email') || '',
      token: hashParams.get('token') || '',
    };
  }, [location.hash]);

  useEffect(() => {
    if (!email || !token) {
      setState('invalid');
      return;
    }

    let isCancelled = false;

    const confirmEmail = async () => {
      try {
        await apiClient.post('/api/v1/users/email-confirmations', { email, token });

        if (isCancelled) {
          return;
        }

        setState('success');
        navigate(ROUTES.EMAIL_SENT.replace('/email-sent', '/email-confirmed') + '?email=' + encodeURIComponent(email), {
          replace: true,
        });
      } catch (error) {
        console.error('Email confirmation failed:', error);
        if (!isCancelled) {
          setState('error');
        }
      }
    };

    void confirmEmail();

    return () => {
      isCancelled = true;
    };
  }, [email, navigate, token]);

  if (state === 'loading') {
    return (
      <div className="relative flex-1 flex items-center justify-center p-4">
        <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />
        <div className="relative z-10 bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6 text-center max-w-md w-full">
          <Loader2 className="h-10 w-10 animate-spin mx-auto mb-4 text-primary-600" />
          <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">
            {t('Confirming your email')}
          </h2>
          <p className="text-content-secondary dark:text-content-secondary-dark">
            {t('Please wait while we verify your confirmation link.')}
          </p>
        </div>
      </div>
    );
  }

  if (state === 'invalid') {
    return (
      <div className="relative flex-1 flex items-center justify-center p-4">
        <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />
        <div className="relative z-10 bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6 text-center max-w-md w-full">
          <MailWarning className="h-10 w-10 mx-auto mb-4 text-amber-500" />
          <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">
            {t('Invalid confirmation link')}
          </h2>
          <p className="text-content-secondary dark:text-content-secondary-dark mb-4">
            {t('This email confirmation link is invalid or incomplete.')}
          </p>
          <Link to={ROUTES.LOGIN} className="text-primary-600 hover:text-primary-700 touch-manipulation">
            {t('Go to login')}
          </Link>
        </div>
      </div>
    );
  }

  if (state === 'error') {
    return (
      <div className="relative flex-1 flex items-center justify-center p-4">
        <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />
        <div className="relative z-10 bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6 text-center max-w-md w-full">
          <MailWarning className="h-10 w-10 mx-auto mb-4 text-red-500" />
          <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">
            {t('Email confirmation failed')}
          </h2>
          <p className="text-content-secondary dark:text-content-secondary-dark mb-4">
            {t('This confirmation link is invalid, expired, or has already been used.')}
          </p>
          <Link to={ROUTES.LOGIN} className="text-primary-600 hover:text-primary-700 touch-manipulation">
            {t('Go to login')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="relative flex-1 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-surface dark:bg-surface-dark" />
      <div className="relative z-10 bg-surface dark:bg-surface-dark rounded-xl border border-outline dark:border-outline-dark p-6 text-center max-w-md w-full">
        <CheckCircle className="h-10 w-10 mx-auto mb-4 text-green-600" />
        <h2 className="text-xl font-semibold text-content dark:text-content-dark mb-2">{t('Email confirmed')}</h2>
      </div>
    </div>
  );
};
