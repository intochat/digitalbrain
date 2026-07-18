import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { handleGoogleSignUp, processGoogleRedirectSignIn } from 'features/auth/lib/oauth';
import {
  consumeTelegramChatId,
  notifyTelegramAfterLogin,
  readTelegramChatIdFromUrl,
  rememberTelegramChatId,
} from 'features/auth/lib/telegramBind';
import { LoadingSpinner } from 'shared/ui';

type Phase = 'initializing' | 'redirecting' | 'binding' | 'done' | 'error';

export const TelegramGoogleAuth = () => {
  const { t } = useFrontendLanguage();
  const [searchParams] = useSearchParams();
  const [phase, setPhase] = useState<Phase>('initializing');
  const [message, setMessage] = useState<string | null>(null);
  const ranRef = useRef(false);

  useEffect(() => {
    if (ranRef.current) return;
    ranRef.current = true;

    const fail = (text: string) => {
      setMessage(text);
      setPhase('error');
    };

    (async () => {
      const redirectResult = await processGoogleRedirectSignIn();

      if (redirectResult) {
        if (!redirectResult.success) {
          fail(redirectResult.error || t('Google sign-in failed'));
          return;
        }

        const chatId = consumeTelegramChatId();
        if (!chatId) {
          fail(t('Missing Telegram chat context. Please reopen the bot and try again.'));
          return;
        }

        setPhase('binding');
        const delivered = await notifyTelegramAfterLogin(chatId);
        if (!delivered) {
          fail(t('Could not deliver the sign-in confirmation. Please reopen the bot and try again.'));
          return;
        }

        setPhase('done');
        return;
      }

      const chatId = readTelegramChatIdFromUrl(searchParams);
      if (!chatId) {
        fail(t('Missing Telegram chat context. Please reopen the bot and try again.'));
        return;
      }

      rememberTelegramChatId(chatId);
      setPhase('redirecting');

      const startResult = await handleGoogleSignUp();
      if (startResult.redirecting) {
        return;
      }
      if (!startResult.success) {
        fail(startResult.error || t('Google sign-in failed'));
      }
    })();
  }, [searchParams, t]);

  return (
    <div className="min-h-[100dvh] flex items-center justify-center bg-surface dark:bg-surface-dark p-6">
      <div className="w-full max-w-sm text-center space-y-4">
        {phase === 'done' ? (
          <>
            <div className="text-4xl">✅</div>
            <h1 className="text-lg font-semibold text-content dark:text-content-dark">
              {t("You're signed in!")}
            </h1>
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
              {t('Return to Telegram — TripRadar is ready to open.')}
            </p>
          </>
        ) : phase === 'error' ? (
          <>
            <div className="text-4xl">⚠️</div>
            <h1 className="text-lg font-semibold text-content dark:text-content-dark">
              {t('Sign-in failed')}
            </h1>
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark">{message}</p>
          </>
        ) : (
          <>
            <LoadingSpinner />
            <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
              {phase === 'binding'
                ? t('Notifying Telegram...')
                : phase === 'redirecting'
                ? t('Redirecting to Google...')
                : t('Preparing sign-in...')}
            </p>
          </>
        )}
      </div>
    </div>
  );
};
