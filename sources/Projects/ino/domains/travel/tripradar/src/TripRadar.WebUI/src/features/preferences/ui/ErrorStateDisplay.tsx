import { AlertCircle, RefreshCw, WifiOff } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { isNetworkError, isServerError, isClientError, getErrorMessage } from '../../../shared/lib/retry/retryUtils';

export interface ErrorStateDisplayProps {
  error: unknown;
  onRetry?: () => void;
  onDismiss?: () => void;
  isRetrying?: boolean;
  retryCount?: number;
  maxRetries?: number;
  className?: string;
}

export const ErrorStateDisplay = ({
  error,
  onRetry,
  onDismiss,
  isRetrying = false,
  retryCount = 0,
  maxRetries = 3,
  className = '',
}: ErrorStateDisplayProps) => {
  const { t } = useFrontendLanguage();
  const errorMessage = getErrorMessage(error);
  const canRetry = onRetry && retryCount < maxRetries;
  const isNetwork = isNetworkError(error);
  const isServer = isServerError(error);
  const isClient = isClientError(error);

  const getErrorTitle = () => {
    if (isNetwork) return t('Connection Problem');
    if (isServer) return t('Server Error');
    if (isClient) return t('Request Error');
    return t('Something Went Wrong');
  };

  const getErrorDescription = () => {
    if (isNetwork) return t('Unable to connect to the server. Please check your internet connection and try again.');
    if (isServer) return t('The server is experiencing issues. This is usually temporary and should resolve shortly.');
    if (isClient) return t('There was a problem with your request. Please check your input and try again.');
    return t('An unexpected error occurred. Please try again or contact support if the problem persists.');
  };

  const ErrorIcon = isNetwork ? WifiOff : AlertCircle;

  return (
    <div className={`border border-red-200 dark:border-red-800/50 rounded-lg p-3 ${className}`}>
      <div className="flex items-start gap-2.5">
        <ErrorIcon className="h-4 w-4 text-red-500 dark:text-red-400 flex-shrink-0 mt-0.5" />

        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-content dark:text-content-dark">{getErrorTitle()}</p>
          <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5">
            {getErrorDescription()}
          </p>

          <details className="mt-2">
            <summary className="text-xs text-content-muted dark:text-content-muted-dark cursor-pointer hover:text-content-secondary dark:hover:text-content-secondary-dark">
              {t('Technical details')}
            </summary>
            <p className="text-xs text-content-muted dark:text-content-muted-dark mt-1 p-2 bg-surface-accent dark:bg-surface-accent-dark rounded font-mono">
              {errorMessage}
            </p>
          </details>

          <div className="flex items-center gap-2 mt-2">
            {canRetry && (
              <button
                onClick={onRetry}
                disabled={isRetrying}
                className="flex items-center gap-1 text-xs font-medium text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <RefreshCw className={`h-3 w-3 ${isRetrying ? 'animate-spin' : ''}`} />
                {isRetrying
                  ? t('Retrying...')
                  : t('Try Again ({attemptsLeft} left)', { attemptsLeft: maxRetries - retryCount })}
              </button>
            )}

            {onDismiss && (
              <button
                onClick={onDismiss}
                className="text-xs text-content-muted dark:text-content-muted-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
              >
                {t('Dismiss')}
              </button>
            )}

            {!canRetry && retryCount >= maxRetries && (
              <span className="text-xs text-content-muted dark:text-content-muted-dark">
                {t('Maximum retry attempts reached')}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
