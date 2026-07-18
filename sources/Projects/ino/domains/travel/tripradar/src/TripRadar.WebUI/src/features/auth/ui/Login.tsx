import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { type LoginError, useLoginMutation } from 'entities/auth';
import { profileApi } from 'entities/user/api';
import { type LinkTelegramResponse } from 'shared/api/types';
import { ROUTES } from 'shared/config/routes';
import { getEmailFromUrlParams, mapProfileToAuthUser } from 'shared/lib';
import { useAuthStore } from 'shared/store/auth';
import { parseBackendError, type ErrorConfig, type NavigationHelpers } from '../lib/errorMessages';
import { handleTelegramAuthSuccess } from '../lib/telegramAuthHelper';
import {
  consumeTelegramChatId,
  notifyTelegramAfterLogin,
  readTelegramChatIdFromUrl,
  rememberTelegramChatId,
} from '../lib/telegramBind';
import { ErrorAlert } from './ErrorAlert';
import { type LoginFormData, LoginForm } from './LoginForm';
import { LoginSuccessState } from './LoginSuccessState';
import { LoginTelegramSection } from './LoginTelegramSection';
import { OAuthButtons } from './OAuthButtons';
import { TelegramConnect } from './TelegramConnect';

export const Login = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const [showTelegramWidget, setShowTelegramWidget] = useState(false);
  const [showTelegramSignIn, setShowTelegramSignIn] = useState(false);
  const [userEmail, setUserEmail] = useState<string>('');
  const [telegramError, setTelegramError] = useState<string>('');
  const [telegramSignInError, setTelegramSignInError] = useState<string>('');
  const [errorConfig, setErrorConfig] = useState<ErrorConfig | null>(null);
  const [inlineError, setInlineError] = useState<string | null>(null);
  const [showSuccessState, setShowSuccessState] = useState(false);
  const [ariaAnnouncement, setAriaAnnouncement] = useState('');
  const login = useAuthStore(state => state.login);
  const loginMutation = useLoginMutation();

  const defaultEmail = getEmailFromUrlParams(searchParams) || '';
  const [telegramBindDone, setTelegramBindDone] = useState(false);

  useEffect(() => {
    const chatId = readTelegramChatIdFromUrl(searchParams);
    if (chatId) rememberTelegramChatId(chatId);
  }, [searchParams]);

  const completeTelegramBindIfPending = async (): Promise<boolean> => {
    const chatId = consumeTelegramChatId();
    if (!chatId) return false;
    const delivered = await notifyTelegramAfterLogin(chatId);
    if (delivered) setTelegramBindDone(true);
    return delivered;
  };
  const dismissError = () => {
    setErrorConfig(null);
    setInlineError(null);
  };
  const dismissTelegram = () => {
    setTelegramError('');
    setShowTelegramWidget(false);
  };
  const dismissTelegramSignIn = () => {
    setTelegramSignInError('');
    setShowTelegramSignIn(false);
  };
  const handleOAuthTelegram = (email: string) => {
    dismissError();
    setTelegramSignInError('');
    setShowTelegramSignIn(false);
    setTelegramError('');
    setUserEmail(email);
    setShowTelegramWidget(true);
  };
  const handleTelegramSignInClick = () => {
    dismissError();
    setTelegramError('');
    setShowTelegramWidget(false);
    setTelegramSignInError('');
    setShowTelegramSignIn(true);
  };

  const navWithEmail = (base: string, email?: string) =>
    navigate(email ? `${base}?email=${encodeURIComponent(email)}` : base);
  const navigationHelpers: NavigationHelpers = {
    navigateToLogin: email => navWithEmail(ROUTES.LOGIN, email),
    navigateToSignup: () => navigate(ROUTES.SIGNUP),
    navigateToPasswordReset: email => navWithEmail(ROUTES.FORGOT_PASSWORD, email),
  };

  useEffect(() => {
    setAriaAnnouncement(
      errorConfig ? t('Error: {title}. {message}', { title: errorConfig.title, message: errorConfig.message }) : ''
    );
  }, [errorConfig, t]);

  const handleLoginError = (error: LoginError, submittedEmail: string) => {
    if (error.isTelegramRequired) {
      const emailForTelegram = error.email?.trim() || submittedEmail.trim();

      if (emailForTelegram) {
        setUserEmail(emailForTelegram);
        setShowTelegramWidget(true);
        return;
      }
    }
    type BackendError = {
      response?: { data?: { code?: string; errorCode?: string; [key: string]: unknown } };
      code?: string;
      message?: string;
    };
    const typed = error as BackendError;
    const errorCode = typed?.response?.data?.code || typed?.response?.data?.errorCode || typed.code;

    // Simple credential errors → inline under password field
    if (errorCode === 'USERNAME_OR_PASSWORD_NOT_VALID' || errorCode === 'PASSWORD_NOT_VALID') {
      setInlineError(t('The email or password you entered is incorrect. Please try again.'));
      setErrorConfig(null);
      return;
    }

    if (errorCode === 'EMAIL_NOT_CONFIRMED') {
      const qs = submittedEmail.trim() ? `?email=${encodeURIComponent(submittedEmail.trim())}` : '';
      setErrorConfig({
        title: t('Email Not Confirmed'),
        message: t('Please confirm your email before logging in. You can request a new confirmation email.'),
        severity: 'warning',
        actions: [
          { label: t('Resend confirmation'), onClick: () => navigate(`${ROUTES.EMAIL_SENT}${qs}`), variant: 'primary' },
        ],
      });
      return;
    }
    const errorWithEmail = {
      ...typed,
      response: { ...typed?.response, data: { ...typed?.response?.data, email: submittedEmail } },
    };
    setErrorConfig(parseBackendError(errorWithEmail, navigationHelpers));
  };

  const handleTelegramSuccess = (response: LinkTelegramResponse) => {
    setShowSuccessState(true);
    const error = handleTelegramAuthSuccess({
      response,
      login,
      navigate: path => navigate(path, { replace: true }),
      targetRoute: ROUTES.PROFILE,
    });
    if (error) {
      setTelegramError(t(error));
      setShowSuccessState(false);
    }
  };

  const handleTelegramSignInSuccess = async () => {
    setShowSuccessState(true);

    try {
      const profile = await profileApi.getProfile({ skipUnauthorizedRedirect: true });
      login(mapProfileToAuthUser(profile));
      navigate(location.state?.from?.pathname || ROUTES.PROFILE, { replace: true });
    } catch {
      setTelegramSignInError(t('Telegram sign-in succeeded, but profile loading failed. Please try again.'));
      setShowSuccessState(false);
    }
  };

  const onSubmit = (data: LoginFormData) => {
    setErrorConfig(null);
    setInlineError(null);
    setTelegramError('');

    loginMutation.mutate(
      { usernameOrEmail: data.usernameOrEmail, password: data.password },
      {
        onSuccess: async () => {
          setShowSuccessState(true);

          try {
            const profile = await profileApi.getProfile();
            login(mapProfileToAuthUser(profile));
          } catch {
            const isEmail = data.usernameOrEmail.includes('@');
            const username = isEmail ? data.usernameOrEmail.split('@')[0] : data.usernameOrEmail;
            login({
              username,
              name: username.charAt(0).toUpperCase() + username.slice(1),
              email: isEmail ? data.usernameOrEmail : '',
              avatar: `https://ui-avatars.com/api/?name=${username}&background=6366f1&color=fff`,
              subscription: 'free',
            });
          }

          const boundToTelegram = await completeTelegramBindIfPending();
          if (boundToTelegram) return;

          navigate(location.state?.from?.pathname || ROUTES.PROFILE, { replace: true });
        },
        onError: (error: LoginError) => handleLoginError(error, data.usernameOrEmail),
      }
    );
  };

  const isPending = loginMutation.isPending;

  if (telegramBindDone) {
    return (
      <div className="min-h-[100dvh] flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
        <div className="w-full max-w-[380px] mx-auto text-center space-y-3">
          <div className="text-4xl">✅</div>
          <h1 className="text-lg font-semibold text-content dark:text-content-dark">{t("You're signed in!")}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Return to Telegram — TripRadar is ready to open.')}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex items-center justify-center p-6 md:p-8 bg-surface dark:bg-surface-dark">
      <div className="w-full max-w-[380px] mx-auto space-y-6">
        <div aria-live="polite" className="sr-only">
          {ariaAnnouncement}
        </div>

        <div className="text-center space-y-1.5">
          <h1 className="text-xl font-semibold text-content dark:text-content-dark">{t('Welcome back')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Sign in to your account to continue your travel journey')}
          </p>
        </div>

        <main>
          <section
            aria-labelledby="oauth-heading"
            className={`transition-opacity duration-200 ${
              isPending || showSuccessState ? 'opacity-50 pointer-events-none' : ''
            }`}
          >
            <h2 id="oauth-heading" className="sr-only">
              {t('Social sign in options')}
            </h2>
            <OAuthButtons
              providers={['google', 'telegram']}
              onTelegramRequired={handleOAuthTelegram}
              onTelegramClick={handleTelegramSignInClick}
            />
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

          {showSuccessState && <LoginSuccessState />}

          {errorConfig && !showSuccessState && (
            <div className="mb-5" role="alert" aria-live="polite" aria-atomic="true">
              <ErrorAlert
                title={errorConfig.title}
                message={errorConfig.message}
                severity={errorConfig.severity}
                actions={errorConfig.actions}
                onDismiss={dismissError}
              />
            </div>
          )}

          <section aria-labelledby="login-form-heading">
            <h2 id="login-form-heading" className="sr-only">
              {t('Email sign in form')}
            </h2>
            <LoginForm
              isPending={isPending}
              showSuccessState={showSuccessState}
              defaultEmail={defaultEmail}
              inlineError={inlineError}
              onSubmit={onSubmit}
            />
          </section>

          <p className="mt-6 text-center text-sm text-content-secondary dark:text-content-secondary-dark">
            <Link
              to={ROUTES.FORGOT_PASSWORD}
              className="hover:text-content dark:hover:text-content-dark transition-colors"
            >
              {t('Forgot password?')}
            </Link>
            <span className="mx-2">·</span>
            <Link
              to={ROUTES.SIGNUP}
              className="text-content dark:text-content-dark font-medium underline underline-offset-2 hover:no-underline"
            >
              {t('Create account')}
            </Link>
          </p>

          {showTelegramWidget && userEmail && (
            <LoginTelegramSection
              userEmail={userEmail}
              onSuccess={handleTelegramSuccess}
              onError={setTelegramError}
              telegramError={telegramError}
              onDismissError={dismissTelegram}
            />
          )}

          {showTelegramSignIn && (
            <section
              className="mt-6 pt-6 border-t border-outline dark:border-outline-dark"
              aria-labelledby="telegram-signin-section-heading"
              role="region"
            >
              <h3 id="telegram-signin-section-heading" className="sr-only">
                {t('Sign in with Telegram')}
              </h3>
              <TelegramConnect
                mode="signIn"
                showRequirementsInfo={false}
                onSuccess={() => {}}
                onError={() => {}}
                onAuthenticated={handleTelegramSignInSuccess}
              />

              {telegramSignInError && (
                <div className="mt-4">
                  <ErrorAlert
                    title={t('Telegram sign-in failed')}
                    message={telegramSignInError}
                    severity="error"
                    actions={[
                      {
                        label: t('Try logging in again'),
                        onClick: dismissTelegramSignIn,
                        variant: 'secondary',
                      },
                    ]}
                    onDismiss={dismissTelegramSignIn}
                  />
                </div>
              )}
            </section>
          )}
        </main>
      </div>
    </div>
  );
};
