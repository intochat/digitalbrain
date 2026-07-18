import { Component, ErrorInfo, ReactNode } from 'react';
import { AlertCircle, RefreshCw } from 'lucide-react';
import { frontendI18n } from 'app/i18n';

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
}

interface State {
  hasError: boolean;
  error?: Error;
  retryCount: number;
}

export class PreferencesErrorBoundary extends Component<Props, State> {
  private maxRetries = 3;

  constructor(props: Props) {
    super(props);
    this.state = {
      hasError: false,
      retryCount: 0,
    };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error, retryCount: 0 };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Preferences Error Boundary caught an error:', error, errorInfo);
    this.props.onError?.(error, errorInfo);
  }

  handleRetry = () => {
    if (this.state.retryCount < this.maxRetries) {
      this.setState(prevState => ({
        hasError: false,
        error: undefined,
        retryCount: prevState.retryCount + 1,
      }));
    }
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      const canRetry = this.state.retryCount < this.maxRetries;

      return (
        <div className="border border-outline dark:border-outline-dark rounded-lg p-4">
          <div className="flex items-start gap-2.5">
            <AlertCircle className="h-4 w-4 text-red-500 dark:text-red-400 flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <p className="text-sm font-medium text-content dark:text-content-dark">
                {frontendI18n.t('Preferences Error')}
              </p>
              <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5">
                {frontendI18n.t(
                  'Something went wrong while loading your preferences. This might be a temporary issue.'
                )}
              </p>

              {this.state.error && (
                <details className="mt-2">
                  <summary className="text-xs text-content-muted dark:text-content-muted-dark cursor-pointer hover:text-content-secondary dark:hover:text-content-secondary-dark">
                    {frontendI18n.t('Technical details')}
                  </summary>
                  <pre className="text-xs text-content-muted dark:text-content-muted-dark mt-1 p-2 bg-surface-accent dark:bg-surface-accent-dark rounded overflow-auto font-mono">
                    {this.state.error.message}
                  </pre>
                </details>
              )}

              <div className="flex gap-2 mt-3">
                {canRetry && (
                  <button
                    onClick={this.handleRetry}
                    className="flex items-center gap-1 text-xs font-medium bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark px-3 py-1.5 rounded-lg hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors"
                  >
                    <RefreshCw className="h-3 w-3" />
                    {frontendI18n.t('Try Again ({attemptsLeft} left)', {
                      attemptsLeft: this.maxRetries - this.state.retryCount,
                    })}
                  </button>
                )}

                <button
                  onClick={() => window.location.reload()}
                  className="text-xs text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark transition-colors"
                >
                  {frontendI18n.t('Reload Page')}
                </button>
              </div>

              {!canRetry && (
                <p className="text-xs text-content-muted dark:text-content-muted-dark mt-2">
                  {frontendI18n.t(
                    'Maximum retry attempts reached. Please reload the page or contact support if the issue persists.'
                  )}
                </p>
              )}
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
