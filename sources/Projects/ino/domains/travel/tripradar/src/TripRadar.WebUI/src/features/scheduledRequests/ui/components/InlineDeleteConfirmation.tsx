import { Button } from 'shared/ui';

interface InlineDeleteConfirmationProps {
  onConfirm: () => void;
  onCancel: () => void;
  isDeleting: boolean;
  t: (key: string) => string;
}

export const InlineDeleteConfirmation = ({ onConfirm, onCancel, isDeleting, t }: InlineDeleteConfirmationProps) => {
  return (
    <div className="flex items-center gap-2">
      <span className="text-sm text-text-secondary dark:text-text-secondary-dark">{t('Delete?')}</span>
      <Button variant="ghost" size="sm" onClick={onConfirm} disabled={isDeleting} isLoading={isDeleting}>
        {t('Yes')}
      </Button>
      <Button variant="ghost" size="sm" onClick={onCancel} disabled={isDeleting}>
        {t('No')}
      </Button>
    </div>
  );
};
