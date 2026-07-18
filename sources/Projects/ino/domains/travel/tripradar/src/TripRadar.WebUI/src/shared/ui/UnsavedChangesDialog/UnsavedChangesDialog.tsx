import { AlertTriangle } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Button } from '../Button';

interface UnsavedChangesDialogProps {
  isOpen: boolean;
  targetPath: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

export const UnsavedChangesDialog = ({ isOpen, targetPath, onConfirm, onCancel }: UnsavedChangesDialogProps) => {
  const { t } = useFrontendLanguage();

  if (!isOpen || !targetPath) {
    return null;
  }

  // Get section name from path for better UX
  const getSectionName = (path: string) => {
    if (path === '/profile') return t('Profile');
    if (path.includes('/security')) return t('Security');
    if (path.includes('/billing')) return t('Billing');
    if (path.includes('/usage')) return t('Usage');
    if (path.includes('/preferences')) return t('Preferences');
    if (path.includes('/privacy')) return t('Privacy');
    return t('another page');
  };

  const sectionName = getSectionName(targetPath);

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/60 dark:bg-black/70 z-50 transition-opacity duration-200"
        onClick={onCancel}
      />

      {/* Dialog */}
      <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          className="bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark rounded-2xl shadow-2xl max-w-md w-full mx-auto transform transition-all duration-200 scale-100"
          onClick={e => e.stopPropagation()}
        >
          <div className="p-6">
            {/* Icon and Title */}
            <div className="flex items-center gap-4 mb-6">
              <div className="p-3 bg-yellow-100 dark:bg-yellow-500/20 rounded-full flex-shrink-0">
                <AlertTriangle className="h-6 w-6 text-yellow-600 dark:text-yellow-400" />
              </div>
              <div>
                <h3 className="text-lg font-semibold text-content dark:text-content-dark">{t('Unsaved Changes')}</h3>
                <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
                  {t('You have unsaved changes that will be lost')}
                </p>
              </div>
            </div>

            {/* Message */}
            <div className="mb-6">
              <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
                {t(
                  'You have unsaved changes on this page. If you navigate to {sectionName}, your changes will be lost.',
                  { sectionName }
                )}
              </p>
            </div>

            {/* Actions */}
            <div className="flex flex-col sm:flex-row gap-3 sm:gap-3 sm:justify-end">
              <Button variant="secondary" size="lg" onClick={onCancel} className="sm:order-1">
                {t('Stay on Page')}
              </Button>
              <Button variant="destructive" size="lg" onClick={onConfirm} className="sm:order-2">
                {t('Discard Changes')}
              </Button>
            </div>
          </div>
        </div>
      </div>
    </>
  );
};
