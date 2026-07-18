import { useEffect, useState } from 'react';
import { Mail } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useResendEmailConfirmationMutation } from 'entities/user/api';
import { ROUTES } from 'shared/config/routes';
import { getEmailFromUrlParams, trackEvent } from 'shared/lib';
import { Button, Input } from 'shared/ui';

const emailRegex = /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i;

export const EmailSent = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const resendMutation = useResendEmailConfirmationMutation();

  const [email, setEmail] = useState('');
  const [feedback, setFeedback] = useState<{ message: string; severity: 'success' | 'error' } | null>(null);

  useEffect(() => {
    const emailFromQuery = getEmailFromUrlParams(searchParams);
    if (emailFromQuery) {
      setEmail(emailFromQuery);
      trackEvent(
        'email_confirmation_pending_viewed',
        { emailSource: 'url_param' },
        { stage: 'activation', userState: 'signed_up' }
      );
      return;
    }

    const emailFromSession = sessionStorage.getItem('registration_email');
    if (emailFromSession) {
      setEmail(emailFromSession);
      trackEvent(
        'email_confirmation_pending_viewed',
        { emailSource: 'session_storage' },
        { stage: 'activation', userState: 'signed_up' }
      );
      return;
    }

    trackEvent(
      'email_confirmation_pending_viewed',
      { emailSource: 'none' },
      { stage: 'activation', userState: 'signed_up' }
    );
  }, [searchParams]);

  const handleResend = async () => {
    const normalizedEmail = email.trim();
    trackEvent(
      'email_confirmation_resend_requested',
      { hasEmailInput: normalizedEmail.length > 0 },
      { stage: 'activation', userState: 'signed_up' }
    );

    if (!normalizedEmail || !emailRegex.test(normalizedEmail)) {
      setFeedback({ message: t('Enter a valid email address to resend confirmation.'), severity: 'error' });
      return;
    }

    try {
      await resendMutation.mutateAsync({ email: normalizedEmail });
      sessionStorage.setItem('registration_email', normalizedEmail);
      trackEvent(
        'email_confirmation_resend_completed',
        { emailDomain: normalizedEmail.split('@')[1] ?? 'unknown' },
        { stage: 'activation', userState: 'signed_up' }
      );
      setFeedback({
        message: t('Request accepted. If delivery is available, a confirmation email will be sent to {email}.', {
          email: normalizedEmail,
        }),
        severity: 'success',
      });
    } catch {
      trackEvent('email_confirmation_resend_failed', {}, { stage: 'activation', userState: 'signed_up' });
      setFeedback({ message: t('Failed to resend confirmation email. Please try again later.'), severity: 'error' });
    }
  };

  return (
    <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
      <div className="w-full max-w-[380px] mx-auto space-y-6">
        <div className="text-center space-y-3">
          <div className="mx-auto w-12 h-12 bg-surface-accent dark:bg-surface-accent-dark rounded-full flex items-center justify-center">
            <Mail className="w-5 h-5 text-content dark:text-content-dark" />
          </div>
          <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Check your email')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark leading-relaxed">
            {t(
              "We've sent you a confirmation link. Please check your email and click the link to verify your account."
            )}
          </p>
        </div>

        <div className="space-y-3">
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t("Didn't receive the email? Check your spam folder or resend below.")}
          </p>

          <div className="flex flex-col gap-1.5">
            <span className="text-sm font-medium text-content dark:text-content-dark">{t('Email address')}</span>
            <Input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder={t('Enter your email')}
              autoComplete="email"
            />
          </div>

          <Button
            onClick={handleResend}
            disabled={resendMutation.isPending}
            isLoading={resendMutation.isPending}
            className="w-full"
          >
            {t('Resend confirmation email')}
          </Button>

          {feedback && (
            <p
              className={`text-xs ${feedback.severity === 'error' ? 'text-red-600 dark:text-red-400' : 'text-emerald-700 dark:text-emerald-400'}`}
              role="status"
            >
              {feedback.message}
            </p>
          )}
        </div>

        <p className="text-center text-sm text-content-secondary dark:text-content-secondary-dark">
          <Link
            to={ROUTES.LOGIN}
            className="underline underline-offset-2 hover:text-content dark:hover:text-content-dark transition-colors"
          >
            {t('Back to Login')}
          </Link>
          <span className="mx-2">·</span>
          <Link
            to={ROUTES.SIGNUP}
            className="underline underline-offset-2 hover:text-content dark:hover:text-content-dark transition-colors"
          >
            {t('Use a different email')}
          </Link>
        </p>
      </div>
    </div>
  );
};
