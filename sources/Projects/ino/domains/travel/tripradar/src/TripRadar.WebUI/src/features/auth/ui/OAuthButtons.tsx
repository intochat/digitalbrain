import { useEffect, useState } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { handleGoogleSignUp, processGoogleRedirectSignIn } from '../lib/oauth';

type OAuthProvider = 'google' | 'telegram';

interface OAuthButtonsProps {
  onTelegramRequired?: (email: string) => void;
  onTelegramClick?: () => void;
  providers?: OAuthProvider[];
}

const defaultProviders: OAuthProvider[] = ['google'];

export const OAuthButtons = ({
  onTelegramRequired,
  onTelegramClick,
  providers = defaultProviders,
}: OAuthButtonsProps) => {
  const { t } = useFrontendLanguage();
  const [oauthError, setOauthError] = useState<string | null>(null);
  const [oauthErrorTitle, setOauthErrorTitle] = useState<string | null>(null);
  const [activeProvider, setActiveProvider] = useState<OAuthProvider | null>(null);
  const supportsGoogle = providers.includes('google');
  const supportsTelegram = providers.includes('telegram');

  useEffect(() => {
    if (!supportsGoogle) {
      return;
    }

    let isActive = true;

    const processRedirectResult = async () => {
      const result = await processGoogleRedirectSignIn();
      if (!isActive || !result) {
        return;
      }

      if (!result.success && result.telegramRequiredEmail) {
        onTelegramRequired?.(result.telegramRequiredEmail);
        return;
      }

      if (!result.success && result.error) {
        setOauthErrorTitle(t('Google sign-in failed'));
        setOauthError(result.error);
      }
    };

    void processRedirectResult();

    return () => {
      isActive = false;
    };
  }, [onTelegramRequired, supportsGoogle, t]);

  const clearError = () => {
    setOauthError(null);
    setOauthErrorTitle(null);
  };

  const handleGoogleClick = async () => {
    clearError();
    setActiveProvider('google');
    let keepSubmitting = false;

    try {
      const result = await handleGoogleSignUp();

      if (result.redirecting) {
        keepSubmitting = true;
        return;
      }

      if (!result.success && result.telegramRequiredEmail) {
        onTelegramRequired?.(result.telegramRequiredEmail);
        return;
      }

      if (!result.success && result.error) {
        console.error('Google sign in failed:', result.error);
        setOauthErrorTitle(t('Google sign-in failed'));
        setOauthError(result.error);
      }
    } catch (error) {
      console.error('Google sign in failed:', error);
      setOauthErrorTitle(t('Google sign-in failed'));
      setOauthError(t('Unexpected Google sign-in error'));
    } finally {
      if (!keepSubmitting) {
        setActiveProvider(null);
      }
    }
  };

  const handleTelegramClick = () => {
    clearError();
    onTelegramClick?.();
  };

  const buttonStyles =
    'w-full flex items-center justify-center space-x-3 px-4 py-4 min-h-[48px] rounded-xl font-medium transition-all duration-200 border border-outline dark:border-outline-dark text-content dark:text-content-dark bg-surface dark:bg-surface-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark hover:border-outline-secondary dark:hover:border-outline-secondary-dark text-base';

  return (
    <div className="space-y-4 mb-6">
      {supportsGoogle && (
        <button onClick={handleGoogleClick} className={buttonStyles} disabled={activeProvider !== null}>
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"
              fill="#4285F4"
            />
            <path
              d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
              fill="#34A853"
            />
            <path
              d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
              fill="#FBBC05"
            />
            <path
              d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
              fill="#EA4335"
            />
          </svg>
          <span>{activeProvider === 'google' ? t('Connecting to Google...') : t('Continue with Google')}</span>
        </button>
      )}

      {supportsTelegram && (
        <button onClick={handleTelegramClick} className={buttonStyles} disabled={activeProvider !== null}>
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M21.3 4.6 3.7 11.3c-1.2.5-1.2 1.2-.2 1.5l4.5 1.4 1.8 5.7c.2.6.1.8.8.8.5 0 .7-.2 1-.5l2.5-2.4 5.1 3.8c.9.5 1.5.2 1.7-.8L23.9 6c.3-1.4-.5-2-1.6-1.4Z" />
          </svg>
          <span>{activeProvider === 'telegram' ? t('Connecting to Telegram...') : t('Continue with Telegram')}</span>
        </button>
      )}

      {oauthError && oauthErrorTitle && (
        <div
          className="rounded-xl border border-red-200 bg-red-50 p-3 dark:border-red-900/70 dark:bg-red-950/30"
          role="alert"
          aria-live="polite"
        >
          <p className="mb-1 text-sm font-semibold text-red-800 dark:text-red-300">{oauthErrorTitle}</p>
          <p className="text-sm text-red-700 dark:text-red-300">{oauthError}</p>
        </div>
      )}
    </div>
  );
};
