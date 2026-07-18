import { ReactNode, useEffect } from 'react';
import { X } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { cn } from 'shared/lib/utils';

interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  size?: 'sm' | 'md' | 'lg' | 'xl';
  showCloseButton?: boolean;
  closeOnBackdropClick?: boolean;
  className?: string;
}

export const Modal = ({
  isOpen,
  onClose,
  title,
  children,
  size = 'md',
  showCloseButton = true,
  closeOnBackdropClick = true,
  className,
}: ModalProps) => {
  const { t } = useFrontendLanguage();

  // Handle escape key
  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleEscape);
      document.body.style.overflow = 'hidden';
    }

    return () => {
      document.removeEventListener('keydown', handleEscape);
      document.body.style.overflow = 'unset';
    };
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  const sizeClasses = {
    sm: 'max-w-sm',
    md: 'max-w-md',
    lg: 'max-w-lg',
    xl: 'max-w-2xl',
  };

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (closeOnBackdropClick && e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/60 dark:bg-black/70 z-50 transition-opacity duration-200"
        onClick={handleBackdropClick}
      />

      {/* Modal */}
      <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby={title ? 'modal-title' : undefined}
          className={cn(
            'bg-surface dark:bg-surface-dark',
            'border border-outline dark:border-outline-dark',
            'rounded-2xl shadow-2xl',
            'w-full mx-auto',
            'transform transition-all duration-200 scale-100',
            'max-h-[90vh] overflow-y-auto',
            sizeClasses[size],
            className
          )}
          onClick={e => e.stopPropagation()}
        >
          {/* Header */}
          {(title || showCloseButton) && (
            <div className="flex items-center justify-between p-6 border-b border-outline dark:border-outline-dark">
              {title && (
                <h2 id="modal-title" className="text-xl font-semibold text-content dark:text-content-dark">
                  {title}
                </h2>
              )}
              {showCloseButton && (
                <button
                  onClick={onClose}
                  className="text-content-muted hover:text-content dark:hover:text-content-dark transition-colors duration-200 p-1.5 rounded-lg hover:bg-surface-accent dark:hover:bg-surface-accent-dark focus:outline-none focus:ring-2 focus:ring-primary-500/20"
                  aria-label={t('Close modal')}
                >
                  <X className="h-5 w-5" />
                </button>
              )}
            </div>
          )}

          {/* Content */}
          <div className="p-6">{children}</div>
        </div>
      </div>
    </>
  );
};
