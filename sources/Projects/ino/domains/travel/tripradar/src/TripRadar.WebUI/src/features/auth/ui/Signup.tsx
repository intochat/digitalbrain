import { useEffect, useRef, useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useRegisterMutation } from 'entities/auth';
import { ROUTES } from 'shared/config/routes';
import { trackEvent } from 'shared/lib';
import { Button, Input } from 'shared/ui';
import { parseBackendError, type ErrorConfig } from '../lib/errorMessages';
import { validatePassword } from '../lib/validation';
import { ErrorAlert } from './ErrorAlert';
import { OAuthButtons } from './OAuthButtons';

interface SignupFormData {
  email: string;
  password: string;
  hasDataStorageConsent: boolean;
}

export const Signup = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const registerMutation = useRegisterMutation();
  const [showPassword, setShowPassword] = useState(false);
  const [errorConfig, setErrorConfig] = useState<ErrorConfig | null>(null);
  const [emailError, setEmailError] = useState<string | null>(null);
  const consentCheckboxRef = useRef<HTMLInputElement>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
    clearErrors,
    setValue,
  } = useForm<SignupFormData>({
    mode: 'onChange',
    defaultValues: { email: '', password: '', hasDataStorageConsent: false },
  });

  useEffect(() => {
    if (errors.hasDataStorageConsent && consentCheckboxRef.current) {
      consentCheckboxRef.current.focus();
    }
  }, [errors.hasDataStorageConsent]);

  const onSubmit = (data: SignupFormData) => {
    if (!data.hasDataStorageConsent) return;
    trackEvent('signup_start', { flow: 'email_password' }, { stage: 'activation', userState: 'anon' });
    setErrorConfig(null);
    setEmailError(null);

    registerMutation.mutate(
      { email: data.email, password: data.password, hasDataStorageConsent: data.hasDataStorageConsent },
      {
        onSuccess: () => {
          trackEvent('signup_complete', { flow: 'email_password' }, { stage: 'activation', userState: 'signed_up' });
          sessionStorage.setItem('registration_email', data.email);
          navigate(ROUTES.EMAIL_SENT);
        },
        onError: (error: unknown) => {
          console.error('Registration failed:', error);
          const backendError = error as {
            response?: { data?: { code?: string; errorCode?: string; [key: string]: unknown } };
            code?: string;
            message?: string;
          };
          const errorWithEmail = {
            ...backendError,
            response: { ...backendError?.response, data: { ...backendError?.response?.data, email: data.email } },
          };
          const errorCode =
            backendError?.response?.data?.code || backendError?.response?.data?.errorCode || backendError?.code;
          if (errorCode === 'USER_EXISTS') {
            setEmailError(t('Account with this email already exists'));
          } else {
            setErrorConfig(parseBackendError(errorWithEmail));
          }
        },
      }
    );
  };

  const isPending = registerMutation.isPending;
  const disabled = isPending;

  return (
    <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
      <div className="w-full max-w-[380px] mx-auto space-y-6">
        <div className="text-center space-y-1.5">
          <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Create your account')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Plan amazing trips with ease')}
          </p>
        </div>

        <main>
          <section aria-labelledby="oauth-heading">
            <h2 id="oauth-heading" className="sr-only">
              {t('Social sign up options')}
            </h2>
            <OAuthButtons />
          </section>

          <div className="relative my-6" role="separator" aria-label={t('Or continue with email')}>
            <div className="absolute inset-0 flex items-center">
              <div className="w-full border-t border-outline dark:border-outline-dark" />
            </div>
            <div className="relative flex justify-center text-xs">
              <span className="px-3 bg-surface dark:bg-surface-dark text-content-muted">
                {t('or continue with email')}
              </span>
            </div>
          </div>

          {errorConfig && (
            <div className="mb-5" role="alert" aria-live="polite" aria-atomic="true">
              <ErrorAlert
                title={errorConfig.title}
                message={errorConfig.message}
                severity={errorConfig.severity}
                actions={errorConfig.actions}
                onDismiss={() => setErrorConfig(null)}
              />
            </div>
          )}

          <section aria-labelledby="signup-form-heading">
            <h2 id="signup-form-heading" className="sr-only">
              {t('Email sign up form')}
            </h2>
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate aria-busy={isPending}>
              <div>
                <label
                  htmlFor="email-input"
                  className="block text-sm font-medium text-content dark:text-content-dark mb-1"
                >
                  {t('Email address')}
                </label>
                <Input
                  {...register('email', {
                    required: true,
                    pattern: { value: /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i, message: t('Invalid email address') },
                    onChange: () => {
                      if (emailError) setEmailError(null);
                    },
                  })}
                  id="email-input"
                  type="email"
                  inputMode="email"
                  autoComplete="email"
                  autoCapitalize="none"
                  autoCorrect="off"
                  spellCheck={false}
                  error={!!errors.email || !!emailError}
                  disabled={disabled}
                  placeholder={t('Enter your email')}
                  aria-describedby={errors.email || emailError ? 'email-error' : undefined}
                  aria-invalid={errors.email || emailError ? 'true' : 'false'}
                  aria-required="true"
                />
                {emailError && (
                  <p id="email-error" className="mt-1 text-xs text-red-500 dark:text-red-400" role="alert">
                    {emailError}
                  </p>
                )}
              </div>

              <div>
                <label
                  htmlFor="password-input"
                  className="block text-sm font-medium text-content dark:text-content-dark mb-1"
                >
                  {t('Password')}
                  <span className="ml-1.5 font-normal text-xs text-content-muted">
                    ({t('9+ characters')}, {t('1 uppercase')}, {t('1 number')}, {t('1 special char')})
                  </span>
                </label>
                <div className="relative">
                  <Input
                    {...register('password', {
                      required: true,
                      validate: value => validatePassword(value).isValid,
                    })}
                    id="password-input"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="new-password"
                    autoCapitalize="none"
                    autoCorrect="off"
                    spellCheck={false}
                    className="pr-10"
                    error={!!errors.password}
                    disabled={disabled}
                    placeholder={t('Create a password')}
                    aria-describedby={errors.password ? 'password-error' : undefined}
                    aria-invalid={errors.password ? 'true' : 'false'}
                    aria-required="true"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword(p => !p)}
                    disabled={disabled}
                    className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-content-muted hover:text-content dark:hover:text-content-dark rounded transition-colors focus:outline-none focus:ring-2 focus:ring-content/10"
                    aria-label={showPassword ? t('Hide password') : t('Show password')}
                    tabIndex={disabled ? -1 : 0}
                  >
                    {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                  </button>
                </div>
              </div>

              <div className="flex items-start gap-2">
                <input
                  {...register('hasDataStorageConsent', { required: t('Please accept our terms to continue') })}
                  ref={consentCheckboxRef}
                  id="consent-checkbox"
                  type="checkbox"
                  className={`mt-0.5 h-4 w-4 rounded border-outline dark:border-outline-dark text-primary-600 focus:ring-2 focus:ring-primary-500/20 transition-colors ${errors.hasDataStorageConsent ? 'border-red-500 dark:border-red-400' : ''}`}
                  aria-describedby="consent-description"
                  aria-invalid={errors.hasDataStorageConsent ? 'true' : 'false'}
                  onChange={e => {
                    setValue('hasDataStorageConsent', e.target.checked);
                    if (e.target.checked && errors.hasDataStorageConsent) clearErrors('hasDataStorageConsent');
                  }}
                />
                <label
                  htmlFor="consent-checkbox"
                  className="text-xs text-content-secondary dark:text-content-secondary-dark leading-relaxed"
                  id="consent-description"
                >
                  {t("I agree to TripRadar's")}{' '}
                  <Link
                    to="/terms"
                    className="text-content dark:text-content-dark underline underline-offset-2 hover:no-underline focus:outline-none focus:ring-2 focus:ring-content/10 rounded px-0.5"
                  >
                    {t('Terms')}
                  </Link>{' '}
                  {t('and')}{' '}
                  <Link
                    to="/privacy"
                    className="text-content dark:text-content-dark underline underline-offset-2 hover:no-underline focus:outline-none focus:ring-2 focus:ring-content/10 rounded px-0.5"
                  >
                    {t('Privacy')}
                  </Link>
                </label>
              </div>

              <Button
                type="submit"
                disabled={disabled}
                isLoading={isPending}
                className="w-full"
                aria-disabled={disabled}
              >
                {isPending ? t('Creating your account...') : t('Get started')}
              </Button>
            </form>
          </section>

          <p className="mt-6 text-center text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Already have an account?')}{' '}
            <Link
              to={ROUTES.LOGIN}
              className="text-content dark:text-content-dark font-medium underline underline-offset-2 hover:no-underline focus:outline-none focus:ring-2 focus:ring-content/10 rounded px-0.5"
            >
              {t('Sign in')}
            </Link>
          </p>
        </main>
      </div>
    </div>
  );
};
