import { useFrontendLanguage } from 'app/providers';

interface LoadingSpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  fullScreen?: boolean;
}

export const LoadingSpinner = ({ size = 'md', className, fullScreen = true }: LoadingSpinnerProps) => {
  const { t } = useFrontendLanguage();

  const sizeClasses = {
    sm: 'h-6 w-6 border-[3px]',
    md: 'h-8 w-8 border-[3px]',
    lg: 'h-12 w-12 border-4',
  };

  const spinner = (
    <div
      className={`animate-spin rounded-full border-outline/30 dark:border-outline-dark/30 border-t-content-secondary dark:border-t-content-secondary-dark ${sizeClasses[size]} ${className || ''}`}
      role="status"
      aria-label={t('Loading')}
    >
      <span className="sr-only">{t('Loading...')}</span>
    </div>
  );

  if (fullScreen) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark">{spinner}</div>
    );
  }

  return spinner;
};
