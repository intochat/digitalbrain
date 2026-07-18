import type { FormEvent } from 'react';
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useForgotPasswordMutation } from 'entities/auth';
import { ROUTES } from 'shared/config/routes';
import { getEmailFromUrlParams } from 'shared/lib';
import { Button, Input } from 'shared/ui';

export const ForgotPassword = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const [email, setEmail] = useState('');
  const [isSubmitted, setIsSubmitted] = useState(false);
  const forgotPasswordMutation = useForgotPasswordMutation();
  const { showError } = useToast();

  useEffect(() => {
    const emailParam = getEmailFromUrlParams(searchParams);
    if (emailParam) setEmail(emailParam);
  }, [searchParams]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    forgotPasswordMutation.mutate(
      { email },
      {
        onSuccess: () => setIsSubmitted(true),
        onError: error => {
          console.error('Forgot password failed:', error);
          showError(t('Failed to send reset email'), t('Please try again.'));
        },
      }
    );
  };

  const isPending = forgotPasswordMutation.isPending;

  if (isSubmitted) {
    return (
      <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
        <div className="w-full max-w-[380px] mx-auto space-y-6">
          <div className="text-center space-y-1.5">
            <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Check your email')}</h1>
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
              {t("We've sent a password reset link to")}
            </p>
          </div>

          <div
            className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-3"
            role="status"
            aria-live="polite"
          >
            <p className="text-sm font-medium text-content dark:text-content-dark">{email}</p>
            <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5">
              {t('Check your inbox and follow the link to reset your password.')}
            </p>
          </div>

          <p className="text-center text-sm text-content-secondary dark:text-content-secondary-dark">
            <Link
              to={ROUTES.LOGIN}
              className="text-content dark:text-content-dark font-medium underline underline-offset-2 hover:no-underline focus:outline-none focus:ring-2 focus:ring-content/10 rounded px-0.5"
            >
              {t('Back to login')}
            </Link>
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
      <div className="w-full max-w-[380px] mx-auto space-y-6">
        <div className="text-center space-y-1.5">
          <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Forgot your password?')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t("Enter your email and we'll send you a reset link")}
          </p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4" noValidate aria-busy={isPending}>
          <div>
            <label htmlFor="email" className="block text-sm font-medium text-content dark:text-content-dark mb-1">
              {t('Email address')}
            </label>
            <Input
              id="email"
              name="email"
              type="email"
              autoComplete="email"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              required
              value={email}
              onChange={e => setEmail(e.target.value)}
              disabled={isPending}
              placeholder={t('Enter your email address')}
              aria-required="true"
            />
          </div>

          <Button type="submit" disabled={isPending} isLoading={isPending} className="w-full" aria-disabled={isPending}>
            {isPending ? t('Sending...') : t('Send reset link')}
          </Button>
        </form>

        <p className="text-center text-sm text-content-secondary dark:text-content-secondary-dark">
          <Link
            to={ROUTES.LOGIN}
            className="text-content dark:text-content-dark font-medium underline underline-offset-2 hover:no-underline focus:outline-none focus:ring-2 focus:ring-content/10 rounded px-0.5"
          >
            {t('Back to login')}
          </Link>
        </p>
      </div>
    </div>
  );
};
