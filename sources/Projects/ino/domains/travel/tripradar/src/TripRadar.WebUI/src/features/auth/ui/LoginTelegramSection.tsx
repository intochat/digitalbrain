import { useFrontendLanguage } from 'app/providers';
import { type LinkTelegramResponse } from 'shared/api/types';
import { ErrorAlert } from './ErrorAlert';
import { TelegramConnect } from './TelegramConnect';

interface LoginTelegramSectionProps {
  userEmail: string;
  onSuccess: (response: LinkTelegramResponse) => void;
  onError: (error: string) => void;
  telegramError: string;
  onDismissError: () => void;
}

export const LoginTelegramSection = ({
  userEmail,
  onSuccess,
  onError,
  telegramError,
  onDismissError,
}: LoginTelegramSectionProps) => {
  const { t } = useFrontendLanguage();

  return (
    <section
      className="mt-6 pt-6 border-t border-outline dark:border-outline-dark"
      aria-labelledby="telegram-section-heading"
      role="region"
    >
      <h3 id="telegram-section-heading" className="sr-only">
        {t('Complete registration with Telegram')}
      </h3>
      <TelegramConnect email={userEmail} showRequirementsInfo={false} onSuccess={onSuccess} onError={onError} />

      {telegramError && (
        <div className="mt-4">
          <ErrorAlert
            title={t('Telegram Connection Failed')}
            message={telegramError}
            severity="error"
            actions={[
              {
                label: t('Try logging in again'),
                onClick: onDismissError,
                variant: 'secondary',
              },
            ]}
            onDismiss={onDismissError}
          />
        </div>
      )}
    </section>
  );
};
