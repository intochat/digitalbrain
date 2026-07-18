import { useState } from 'react';
import { AlertTriangle, Trash2 } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useDeleteAccountMutation } from 'entities/user/api';
import { useAuthStore } from 'shared/store/auth';
import { Button, Input, Modal } from 'shared/ui';

const getApiValidationMessage = (error: unknown): string | null => {
  if (!error || typeof error !== 'object') return null;
  const typedError = error as { response?: { data?: { detail?: string; message?: string } } };
  return typedError.response?.data?.detail ?? typedError.response?.data?.message ?? null;
};

export const DangerZoneSection = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const { user, logout } = useAuthStore();
  const { showError, showSuccess } = useToast();
  const deleteAccountMutation = useDeleteAccountMutation();
  const isDeletingAccount = deleteAccountMutation.isPending;

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [confirmValue, setConfirmValue] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const isConfirmValid = !!user?.username && confirmValue.trim().toLowerCase() === user.username.trim().toLowerCase();

  const openModal = () => {
    setValidationError(null);
    setConfirmValue('');
    setIsModalOpen(true);
  };

  const closeModal = () => {
    if (isDeletingAccount) return;
    setIsModalOpen(false);
    setConfirmValue('');
    setValidationError(null);
  };

  const handleDelete = async () => {
    if (!user?.username) {
      showError(t('Error'), t('User not found'));
      return;
    }
    if (confirmValue.trim().toLowerCase() !== user.username.trim().toLowerCase()) {
      setValidationError(t('Type "{username}" to confirm account deletion.', { username: user.username }));
      return;
    }
    try {
      await deleteAccountMutation.mutateAsync();
      showSuccess(t('Account deleted'), t('Your account has been deleted.'));
      logout();
      navigate('/', { replace: true });
    } catch (error) {
      const msg = getApiValidationMessage(error);
      setValidationError(msg ?? t('Failed to delete account. Please try again.'));
      console.error('Failed to delete account:', error);
    }
  };

  return (
    <>
      <section>
        <h3 className="text-sm font-semibold text-content dark:text-content-dark mb-1">{t('Danger Zone')}</h3>
        <p className="text-xs text-content-muted dark:text-content-muted-dark mb-3">
          {t('Permanently delete your account and all associated data. This action cannot be undone.')}
        </p>
        <button
          type="button"
          onClick={openModal}
          className="px-3 py-1.5 text-xs font-medium rounded-lg text-red-600 dark:text-red-400 border border-red-200 dark:border-red-500/30 hover:bg-red-50 dark:hover:bg-red-500/10 transition-colors"
        >
          {t('Delete account')}
        </button>
      </section>

      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title={t('Delete account')}
        closeOnBackdropClick={!isDeletingAccount}
      >
        <div className="space-y-4">
          <div className="flex items-start gap-3 p-3 rounded-lg bg-red-50 dark:bg-red-500/10 border border-red-200 dark:border-red-500/20">
            <AlertTriangle className="h-4 w-4 text-red-600 dark:text-red-400 mt-0.5 flex-shrink-0" />
            <p className="text-sm text-red-700 dark:text-red-300">
              {t('This will permanently remove your account, profile, trips, and billing preferences.')}
            </p>
          </div>
          <div className="space-y-1.5">
            <label
              htmlFor="delete-account-confirmation"
              className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark"
            >
              {t('Type your username to confirm')}
            </label>
            <Input
              id="delete-account-confirmation"
              value={confirmValue}
              onChange={e => {
                setConfirmValue(e.target.value);
                if (validationError) setValidationError(null);
              }}
              disabled={isDeletingAccount}
              placeholder={user?.username ?? t('username')}
              autoComplete="off"
              error={!!validationError}
            />
          </div>
          {validationError && (
            <p className="text-xs text-red-600 dark:text-red-400" role="alert">
              {validationError}
            </p>
          )}
          <div className="flex justify-end gap-2 pt-1">
            <Button variant="ghost" size="sm" onClick={closeModal} disabled={isDeletingAccount}>
              {t('Cancel')}
            </Button>
            <Button
              variant="destructive"
              size="sm"
              onClick={handleDelete}
              disabled={isDeletingAccount || !isConfirmValid}
              isLoading={isDeletingAccount}
            >
              <Trash2 className="h-3.5 w-3.5" />
              {t('Delete account permanently')}
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
};
