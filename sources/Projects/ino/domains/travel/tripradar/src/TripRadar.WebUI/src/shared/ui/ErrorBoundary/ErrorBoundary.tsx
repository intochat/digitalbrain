import { Component, ErrorInfo, ReactNode } from 'react';
import { frontendI18n } from 'app/i18n';
import { Button } from '../Button';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Error caught by boundary:', error, errorInfo);
    console.error('Error stack:', error.stack);
    console.error('Component stack:', errorInfo.componentStack);
  }

  render() {
    const t = frontendI18n.t.bind(frontendI18n);

    if (this.state.hasError) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark">
          <div className="text-center max-w-md mx-auto p-6">
            <h1 className="text-2xl font-semibold text-content dark:text-content-dark mb-4">
              {t('Something went wrong')}
            </h1>
            <p className="text-content-secondary dark:text-content-secondary-dark mb-6">
              {t('An error occurred while loading this page. Please check the browser console for more details.')}
            </p>
            <div className="space-y-3">
              <Button onClick={() => this.setState({ hasError: false })} variant="primary" size="md" className="w-full">
                {t('Try again')}
              </Button>
              <Button onClick={() => window.location.reload()} variant="secondary" size="md" className="w-full">
                {t('Reload page')}
              </Button>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
