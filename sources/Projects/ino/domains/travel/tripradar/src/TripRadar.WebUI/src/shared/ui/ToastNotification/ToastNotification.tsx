import { useEffect } from 'react';
import { AlertCircle, CheckCircle, Info, X } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

export type ToastType = 'success' | 'error' | 'info';

export interface Toast {
  id: string;
  type: ToastType;
  title: string;
  message?: string;
  duration?: number;
  onDismiss?: () => void;
}

interface ToastNotificationProps extends Toast {
  onClose: (id: string) => void;
}

const iconMap: Record<ToastType, { Icon: typeof CheckCircle; className: string }> = {
  success: { Icon: CheckCircle, className: 'text-emerald-600 dark:text-emerald-400' },
  error: { Icon: AlertCircle, className: 'text-red-600 dark:text-red-400' },
  info: { Icon: Info, className: 'text-content-muted dark:text-content-muted-dark' },
};

export const ToastNotification = ({ id, type, title, message, duration = 5000, onClose }: ToastNotificationProps) => {
  const { t } = useFrontendLanguage();

  useEffect(() => {
    if (duration > 0) {
      const timer = setTimeout(() => onClose(id), duration);
      return () => clearTimeout(timer);
    }
  }, [id, duration, onClose]);

  const { Icon, className: iconClass } = iconMap[type];

  return (
    <div
      className="bg-surface dark:bg-surface-accent-dark border border-outline dark:border-outline-dark rounded-lg p-3 shadow-sm animate-in slide-in-from-right-full duration-200"
      role="alert"
      aria-live="polite"
    >
      <div className="flex items-start gap-2.5">
        <Icon className={`h-4 w-4 mt-0.5 flex-shrink-0 ${iconClass}`} aria-hidden="true" />
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-content dark:text-content-dark">{title}</p>
          {message && <p className="text-xs text-content-muted dark:text-content-muted-dark mt-0.5">{message}</p>}
        </div>
        <button
          onClick={() => onClose(id)}
          className="flex-shrink-0 p-1 rounded-md text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-outline dark:focus-visible:ring-outline-dark"
          aria-label={t('Dismiss notification')}
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
};
