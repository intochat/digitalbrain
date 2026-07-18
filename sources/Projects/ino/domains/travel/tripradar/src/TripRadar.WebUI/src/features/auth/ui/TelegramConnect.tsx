import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useFrontendLanguage } from 'app/providers';
import {
  useLinkTelegramMutation,
  useSyncTelegramUsernameMutation,
  useTelegramWidgetSignInMutation,
} from 'entities/auth';
import type { LinkTelegramResponse, TelegramAuthApiData, TelegramData } from 'shared/api/types';
import { isTelegramLocalWidgetEnabled } from 'shared/config/env';
import { trackEvent } from 'shared/lib';
import { LoadingSpinner } from 'shared/ui';
import {
  buildTelegramAuthUrl,
  convertTelegramDataToApiFormat,
  getTelegramBotUsername,
  loadTelegramWidget,
  validateTelegramData,
} from '../lib/telegram';
import { ErrorAlert } from './ErrorAlert';

/**
 * Telegram error state interface
 */
interface TelegramErrorState {
  hasError: boolean;
  errorMessage: string;
  retryCount: number;
  troubleshootingSteps: string[];
  domainCandidates?: string[];
  domainCommand?: string;
}

/**
 * Props for TelegramConnect component
 */
interface TelegramConnectProps {
  email?: string;
  mode?: 'activation' | 'usernameSync' | 'signIn';
  showRequirementsInfo?: boolean;
  onSuccess: (response: LinkTelegramResponse) => void;
  onError: (error: string) => void;
  onAuthenticated?: () => void | Promise<void>;
}

interface TelegramMiniAppUserPayload {
  id?: number | string;
  first_name?: string;
  last_name?: string;
  username?: string;
  photo_url?: string;
}

const parseMiniAppTelegramAuthData = (): TelegramAuthApiData | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  const telegramWebApp = (window as Window & { Telegram?: { WebApp?: { initData?: string } } }).Telegram?.WebApp;
  const initData = telegramWebApp?.initData?.trim();
  if (!initData) {
    return null;
  }

  const params = new URLSearchParams(initData);
  const userValue = params.get('user');
  const hash = params.get('hash');
  const authDateRaw = params.get('auth_date');

  if (!userValue || !hash || !authDateRaw) {
    return null;
  }

  const authDate = Number(authDateRaw);
  if (!Number.isFinite(authDate) || authDate <= 0) {
    return null;
  }

  try {
    const parsedUser = JSON.parse(userValue) as TelegramMiniAppUserPayload;
    const id = Number(parsedUser.id);

    if (!Number.isFinite(id) || id <= 0) {
      return null;
    }

    return {
      id,
      firstName: parsedUser.first_name ?? '',
      lastName: parsedUser.last_name ?? null,
      username: parsedUser.username ?? '',
      photoUrl: parsedUser.photo_url ?? null,
      authDate,
      hash,
      rawInitData: initData,
    };
  } catch {
    return null;
  }
};

/**
 * TroubleshootingSteps component for displaying error recovery instructions
 */
const TroubleshootingSteps = ({ steps, title }: { steps: string[]; title: string }) => (
  <div className="mt-3">
    <p className="text-xs font-medium text-content dark:text-content-dark mb-1.5">{title}</p>
    <ol className="text-xs text-content-muted dark:text-content-muted-dark space-y-1">
      {steps.map((step, index) => (
        <li key={index} className="flex items-start gap-1.5">
          <span className="text-content-secondary dark:text-content-secondary-dark flex-shrink-0">{index + 1}.</span>
          <span>{step}</span>
        </li>
      ))}
    </ol>
  </div>
);

/**
 * TelegramConnect Component
 *
 * Renders the Telegram Login Widget and handles the OAuth flow for linking
 * a Telegram account to a user after email confirmation or login attempt.
 *
 * Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 5.1, 5.2, 5.3, 5.4, 5.5
 *
 * @param email - User's email to identify the user
 * @param onSuccess - Callback when Telegram linking succeeds
 * @param onError - Callback when an error occurs
 *
 * @example
 * <TelegramConnect
 *   email={userEmail}
 *   onSuccess={(response) => {
 *     // Store tokens and redirect
 *     navigate('/profile');
 *   }}
 *   onError={(error) => {
 *     // Show error message
 *     setError(error);
 *   }}
 * />
 */
export const TelegramConnect = ({
  email,
  mode = 'activation',
  showRequirementsInfo = true,
  onSuccess,
  onError,
  onAuthenticated,
}: TelegramConnectProps) => {
  const { t } = useFrontendLanguage();
  const [isLoading, setIsLoading] = useState(false);
  const [scriptLoaded, setScriptLoaded] = useState(false);
  const [telegramError, setTelegramError] = useState<TelegramErrorState>({
    hasError: false,
    errorMessage: '',
    retryCount: 0,
    troubleshootingSteps: [],
  });
  const widgetContainerRef = useRef<HTMLDivElement>(null);
  const domainErrorDetectedRef = useRef(false);
  const { mutate: linkTelegram, isPending: isActivationPending } = useLinkTelegramMutation();
  const { mutate: syncTelegramUsername, isPending: isUsernameSyncPending } = useSyncTelegramUsernameMutation();
  const { mutate: signInWithTelegram, isPending: isSignInPending } = useTelegramWidgetSignInMutation();
  const isPending = isActivationPending || isUsernameSyncPending || isSignInPending;
  const currentHost = typeof window !== 'undefined' ? window.location.host : '';
  const currentHostname = typeof window !== 'undefined' ? window.location.hostname : '';
  const hostWithoutPort = currentHostname || (currentHost.includes(':') ? currentHost.split(':')[0] : currentHost);
  const isLocalHost = useMemo(() => {
    const normalizedHost = hostWithoutPort.toLowerCase();
    return (
      normalizedHost === 'localhost' ||
      normalizedHost === '127.0.0.1' ||
      normalizedHost === '::1' ||
      normalizedHost === '[::1]' ||
      normalizedHost.endsWith('.localhost')
    );
  }, [hostWithoutPort]);
  const miniAppAuthData = useMemo(() => parseMiniAppTelegramAuthData(), []);
  const isMiniAppContext = miniAppAuthData !== null;
  const localWidgetEnabled = useMemo(() => isTelegramLocalWidgetEnabled(), []);
  const domainCandidates = useMemo(
    () => [currentHost, hostWithoutPort].filter((value, index, self) => !!value && self.indexOf(value) === index),
    [currentHost, hostWithoutPort]
  );
  const setDomainCandidates = useMemo(
    () =>
      [hostWithoutPort, currentHost.includes(':') ? '' : currentHost].filter(
        (value, index, self) => !!value && self.indexOf(value) === index
      ),
    [currentHost, hostWithoutPort]
  );

  /**
   * Handle Telegram connection errors with troubleshooting steps
   */
  const handleTelegramError = useCallback(
    (error: Error | string, domainContext?: { candidates: string[]; command: string }) => {
      const errorMessage = typeof error === 'string' ? error : error.message;

      const troubleshootingSteps = [
        t('Ensure you have a Telegram account'),
        t('Check that pop-ups are not blocked in your browser'),
        t('Ensure BotFather /setdomain includes "{host}"', {
          host: hostWithoutPort || currentHost || t('your app host'),
        }),
        t('Try refreshing the page'),
        t('Clear browser cache and cookies'),
        t('Disable browser extensions temporarily'),
      ];

      setTelegramError({
        hasError: true,
        errorMessage: errorMessage || t('Failed to connect Telegram'),
        retryCount: telegramError.retryCount + 1,
        troubleshootingSteps,
        domainCandidates: domainContext?.candidates,
        domainCommand: domainContext?.command,
      });

      // Also call the original onError callback for backward compatibility
      onError(errorMessage || t('Failed to connect Telegram'));
    },
    [currentHost, hostWithoutPort, onError, t, telegramError.retryCount]
  );

  /**
   * Handle retry - clear error state and reinitialize widget
   */
  const handleRetry = useCallback(() => {
    setTelegramError({
      hasError: false,
      errorMessage: '',
      retryCount: 0,
      troubleshootingSteps: [],
      domainCandidates: [],
      domainCommand: '',
    });
    setScriptLoaded(false);
    setIsLoading(false);
    domainErrorDetectedRef.current = false;

    // Clear the widget container
    if (widgetContainerRef.current) {
      widgetContainerRef.current.innerHTML = '';
    }

    // Reinitialize will happen via useEffect dependency change
  }, []);

  const submitTelegramAuth = useCallback(
    (telegramAuth: TelegramAuthApiData) => {
      trackEvent(
        'telegram_connect',
        {
          lifecycleStatus: 'started',
          mode,
          context: isMiniAppContext ? 'mini_app' : 'widget',
        },
        { stage: 'activation', userState: 'signed_up' }
      );

      setIsLoading(true);

      if (mode === 'activation') {
        if (!email) {
          setIsLoading(false);
          handleTelegramError(t('Email is required to complete Telegram activation.'));
          return;
        }

        // Call Telegram account activation API.
        linkTelegram(
          {
            email,
            telegramAuth,
          },
          {
            onSuccess: response => {
              setIsLoading(false);
              trackEvent(
                'telegram_connect',
                {
                  lifecycleStatus: 'completed',
                  mode: 'activation',
                  context: isMiniAppContext ? 'mini_app' : 'widget',
                },
                { stage: 'activation', userState: 'activated' }
              );

              // Call success callback
              onSuccess(response);
            },
            onError: error => {
              console.error('❌ Telegram linking failed:', error);
              setIsLoading(false);
              trackEvent(
                'telegram_connect',
                {
                  lifecycleStatus: 'failed',
                  mode: 'activation',
                  context: isMiniAppContext ? 'mini_app' : 'widget',
                  reason: error instanceof Error ? error.message : 'unknown_error',
                },
                { stage: 'activation', userState: 'signed_up' }
              );

              // Extract error message
              const errorMessage =
                error instanceof Error ? error.message : t('Failed to link Telegram account. Please try again.');

              // Use the new error handler
              handleTelegramError(errorMessage);
            },
          }
        );
        return;
      }

      if (mode === 'signIn') {
        if (!telegramAuth.username?.trim()) {
          setIsLoading(false);
          handleTelegramError(t('Telegram username is required. Please set a username in Telegram and try again.'));
          return;
        }

        signInWithTelegram(
          {
            telegramAuth,
          },
          {
            onSuccess: async () => {
              setIsLoading(false);
              trackEvent(
                'telegram_connect',
                {
                  lifecycleStatus: 'completed',
                  mode: 'signIn',
                  context: isMiniAppContext ? 'mini_app' : 'widget',
                },
                { stage: 'activation', userState: 'activated' }
              );

              await onAuthenticated?.();
            },
            onError: error => {
              console.error('❌ Telegram sign-in failed:', error);
              setIsLoading(false);
              trackEvent(
                'telegram_connect',
                {
                  lifecycleStatus: 'failed',
                  mode: 'signIn',
                  context: isMiniAppContext ? 'mini_app' : 'widget',
                  reason: error instanceof Error ? error.message : 'unknown_error',
                },
                { stage: 'activation', userState: 'signed_up' }
              );

              const errorMessage =
                error instanceof Error ? error.message : t('Failed to sign in with Telegram. Please try again.');

              handleTelegramError(errorMessage);
            },
          }
        );
        return;
      }

      // Call Telegram username sync API.
      syncTelegramUsername(
        {
          telegramAuth,
        },
        {
          onSuccess: response => {
            setIsLoading(false);
            trackEvent(
              'telegram_connect',
              {
                lifecycleStatus: 'completed',
                mode: 'usernameSync',
                context: isMiniAppContext ? 'mini_app' : 'widget',
              },
              { stage: 'activation', userState: 'activated' }
            );
            onSuccess(response);
          },
          onError: error => {
            console.error('❌ Telegram username sync failed:', error);
            setIsLoading(false);
            trackEvent(
              'telegram_connect',
              {
                lifecycleStatus: 'failed',
                mode: 'usernameSync',
                context: isMiniAppContext ? 'mini_app' : 'widget',
                reason: error instanceof Error ? error.message : 'unknown_error',
              },
              { stage: 'activation', userState: 'signed_up' }
            );

            const errorMessage =
              error instanceof Error ? error.message : t('Failed to sync Telegram username. Please try again.');

            handleTelegramError(errorMessage);
          },
        }
      );
    },
    [
      email,
      handleTelegramError,
      isMiniAppContext,
      linkTelegram,
      mode,
      onAuthenticated,
      onSuccess,
      signInWithTelegram,
      syncTelegramUsername,
      t,
    ]
  );

  /**
   * Handle Telegram OAuth callback
   * This function is called by the Telegram widget when user authorizes
   */
  const handleTelegramAuth = useCallback(
    (user: TelegramData) => {
      // Validate telegram data structure
      if (!validateTelegramData(user)) {
        console.error('❌ Invalid Telegram data structure:', user);
        handleTelegramError(t('Invalid data received from Telegram. Please try again.'));
        return;
      }

      const telegramAuth = convertTelegramDataToApiFormat(user);
      submitTelegramAuth(telegramAuth);
    },
    [handleTelegramError, submitTelegramAuth, t]
  );

  /**
   * Load Telegram widget script and render widget
   */
  useEffect(() => {
    const initializeTelegramWidget = async () => {
      try {
        if (mode === 'activation') {
          if (!email) {
            handleTelegramError(t('Email is required to complete Telegram activation.'));
            return;
          }

          // Store email in sessionStorage for OAuth redirect callback
          sessionStorage.setItem('telegram_auth_email', email);
        }

        if (miniAppAuthData) {
          if (!miniAppAuthData.username?.trim()) {
            handleTelegramError(t('Telegram username is required. Please set a username in Telegram and try again.'));
            return;
          }

          submitTelegramAuth(miniAppAuthData);
          return;
        }

        // Telegram Login Widget cannot be validated on localhost domains.
        // In local development, users should open the app inside Telegram Mini App.
        if (isLocalHost && !localWidgetEnabled) {
          setScriptLoaded(true);
          return;
        }

        // Load Telegram widget script
        await loadTelegramWidget();
        setScriptLoaded(true);

        // Get bot username from environment
        const botUsername = getTelegramBotUsername();

        // Set up global callback for Telegram widget
        window.onTelegramAuth = handleTelegramAuth;

        // Render Telegram widget
        if (widgetContainerRef.current) {
          // Clear any existing content
          widgetContainerRef.current.innerHTML = '';

          // Create script element for widget
          const script = document.createElement('script');
          script.src = 'https://telegram.org/js/telegram-widget.js?22';
          script.async = true;
          script.setAttribute('data-telegram-login', botUsername);
          script.setAttribute('data-size', 'large');

          // Use callback auth for sign-in and keep redirect auth for activation/sync flows.
          script.setAttribute('data-onauth', 'onTelegramAuth(user)');
          if (mode !== 'signIn') {
            const callbackPath = mode === 'activation' ? '/auth/telegram-callback' : '/auth/telegram-username-sync';
            script.setAttribute('data-auth-url', buildTelegramAuthUrl(callbackPath));
          }
          script.setAttribute('data-request-access', 'write');
          script.setAttribute('data-lang', 'en'); // Force English language

          widgetContainerRef.current.appendChild(script);
        }
      } catch (error) {
        console.error('❌ Failed to load Telegram widget:', error);
        handleTelegramError(t('Failed to load Telegram widget. Please refresh the page and try again.'));
      }
    };

    initializeTelegramWidget();

    // Cleanup
    return () => {
      if (window.onTelegramAuth) {
        delete window.onTelegramAuth;
      }
      domainErrorDetectedRef.current = false;
    };
  }, [
    email,
    handleTelegramAuth,
    handleTelegramError,
    isLocalHost,
    localWidgetEnabled,
    miniAppAuthData,
    mode,
    submitTelegramAuth,
    t,
    telegramError.hasError,
  ]); // Re-initialize if config changes or after retry

  useEffect(() => {
    const container = widgetContainerRef.current;
    if (!container) {
      return;
    }

    const detectDomainError = () => {
      const text = container.textContent?.toLowerCase() ?? '';
      if (!text.includes('bot domain invalid') || domainErrorDetectedRef.current) {
        return;
      }

      domainErrorDetectedRef.current = true;
      const candidatesForSetDomain = setDomainCandidates.length ? setDomainCandidates : domainCandidates;
      const domainsText = candidatesForSetDomain.map(candidate => `"${candidate}"`).join(' or ');
      const commandText = candidatesForSetDomain.map(candidate => `/setdomain ${candidate}`).join(' | ');
      handleTelegramError(
        t('Telegram rejected this host. Configure BotFather to run {commandText} for {domainsText}.', {
          commandText: commandText || t('/setdomain your-app-host'),
          domainsText: domainsText || t('your current host'),
        }),
        { candidates: candidatesForSetDomain, command: commandText }
      );
    };

    detectDomainError();

    const observer = new MutationObserver(() => {
      detectDomainError();
    });

    observer.observe(container, {
      childList: true,
      subtree: true,
      characterData: true,
    });

    return () => observer.disconnect();
  }, [domainCandidates, handleTelegramError, setDomainCandidates, t]);

  return (
    <div className="flex flex-col gap-3" data-testid="telegram-connect">
      {!telegramError.hasError && showRequirementsInfo && (
        <div className="rounded-lg border border-outline/50 dark:border-outline-dark/50 bg-surface dark:bg-surface-dark p-3">
          <p className="text-[11px] font-medium uppercase tracking-wider text-content-muted dark:text-content-muted-dark mb-1.5">
            {t('Why Telegram connection is required')}
          </p>
          <ul className="space-y-1 text-xs text-content-secondary dark:text-content-secondary-dark">
            <li>{t('Secure identity check between your TripRadar account and Telegram profile.')}</li>
            <li>{t('Enables Telegram-native planning flow, reminders, and trip delivery.')}</li>
            <li>{t('Lets TripRadar keep your plans in one reusable trip vault context.')}</li>
          </ul>
        </div>
      )}

      {telegramError.hasError && (
        <div className="w-full">
          <ErrorAlert
            title={t('Telegram Connection Failed')}
            message={telegramError.errorMessage}
            severity="error"
            actions={[
              {
                label: t('Try Again'),
                onClick: handleRetry,
                variant: 'primary',
              },
            ]}
          >
            <TroubleshootingSteps steps={telegramError.troubleshootingSteps} title={t('Try these steps:')} />
            {!!telegramError.domainCandidates?.length && (
              <div className="mt-2">
                <p className="text-xs font-medium text-content dark:text-content-dark mb-1">
                  {t('BotFather commands:')}
                </p>
                <div className="space-y-0.5">
                  {telegramError.domainCandidates.map(domain => (
                    <code
                      key={domain}
                      className="block rounded bg-surface-accent dark:bg-surface-accent-dark px-2 py-1 text-[11px] font-mono text-content-secondary dark:text-content-secondary-dark"
                    >
                      /setdomain {domain}
                    </code>
                  ))}
                </div>
              </div>
            )}
          </ErrorAlert>
        </div>
      )}

      {!telegramError.hasError && (isLoading || isPending) && (
        <div className="flex items-center gap-2 py-2">
          <LoadingSpinner />
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            {t('Connecting your Telegram account...')}
          </p>
        </div>
      )}

      {!telegramError.hasError && !isLoading && !isPending && (
        <div className="flex flex-col items-center gap-3">
          {!isMiniAppContext && <div ref={widgetContainerRef} className="flex justify-center" />}
          {!isMiniAppContext && !isLocalHost && !scriptLoaded && (
            <div className="flex items-center gap-2">
              <LoadingSpinner />
              <p className="text-xs text-content-muted dark:text-content-muted-dark">
                {t('Loading Telegram widget...')}
              </p>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
