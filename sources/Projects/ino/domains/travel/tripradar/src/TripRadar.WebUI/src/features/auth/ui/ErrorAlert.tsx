import React from 'react';
import { AlertCircle, AlertTriangle, Info, X } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Button } from 'shared/ui';
import type { ErrorConfig } from '../lib/errorMessages';

interface ErrorAlertProps extends ErrorConfig {
  onDismiss?: () => void;
  children?: React.ReactNode;
}

const SEVERITY_ICON = { error: AlertCircle, warning: AlertTriangle, info: Info };

const SEVERITY_DOT: Record<ErrorConfig['severity'], string> = {
  error: 'bg-red-500',
  warning: 'bg-amber-500',
  info: 'bg-blue-500',
};

export const ErrorAlert = ({ title, message, severity, actions, onDismiss, children }: ErrorAlertProps) => {
  const { t } = useFrontendLanguage();
  const Icon = SEVERITY_ICON[severity];

  return (
    <div
      className="rounded-lg border border-outline/50 dark:border-outline-dark/50 bg-surface dark:bg-surface-dark p-4"
      role="alert"
      aria-live="polite"
    >
      <div className="flex items-start gap-3">
        <div className="flex items-center gap-2 mt-0.5 shrink-0">
          <span className={`h-1.5 w-1.5 rounded-full ${SEVERITY_DOT[severity]}`} />
          <Icon className="h-4 w-4 text-content-muted dark:text-content-muted-dark" aria-hidden="true" />
        </div>

        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-content dark:text-content-dark">{title}</p>
          <p className="text-xs text-content-secondary dark:text-content-secondary-dark mt-0.5 leading-relaxed">
            {message}
          </p>
          {children}
          {actions && actions.length > 0 && (
            <div className="flex flex-wrap gap-2 mt-3">
              {actions.map((action, index) => (
                <Button
                  key={index}
                  variant={action.variant === 'primary' ? 'primary' : 'secondary'}
                  size="sm"
                  onClick={action.onClick}
                >
                  {action.label}
                </Button>
              ))}
            </div>
          )}
        </div>

        {onDismiss && (
          <button
            onClick={onDismiss}
            className="shrink-0 p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
            aria-label={t('Dismiss alert')}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
    </div>
  );
};
