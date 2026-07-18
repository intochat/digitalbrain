import { AlertCircle } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useAuthStore } from 'shared/store/auth';
import { FeedbackForm } from './FeedbackForm';

export interface FeedbackSectionProps {
  className?: string;
}

export const FeedbackSection = ({ className = '' }: FeedbackSectionProps) => {
  const { t } = useFrontendLanguage();
  const { user } = useAuthStore();
  const isAuthenticated = Boolean(user);

  return (
    <div className={className}>
      {!isAuthenticated && (
        <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5">
          <div className="flex items-center gap-3 text-content-secondary dark:text-content-secondary-dark">
            <AlertCircle className="h-4 w-4 flex-shrink-0" />
            <p className="text-sm">{t('Sign in to submit feedback with your Telegram username.')}</p>
          </div>
        </div>
      )}

      {isAuthenticated && <FeedbackForm />}
    </div>
  );
};
