import { useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { frontendI18n } from 'app/i18n';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useChangePasswordMutation } from 'entities/user/api';
import { Button, Input } from 'shared/ui';

interface PasswordFormState {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

interface ApiErrorResponseShape {
  errorCode?: string;
  code?: string;
  message?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

interface ApiErrorShape {
  code?: string;
  response?: {
    data?: ApiErrorResponseShape;
  };
}

const getApiErrorCode = (error: unknown): string | null => {
  if (!error || typeof error !== 'object') return null;
  const typedError = error as ApiErrorShape;
  return typedError.code ?? typedError.response?.data?.errorCode ?? typedError.response?.data?.code ?? null;
};

const getApiValidationMessage = (error: unknown): string | null => {
  if (!error || typeof error !== 'object') return null;
  const typedError = error as ApiErrorShape;
  const responseData = typedError.response?.data;
  if (!responseData) return null;
  if (responseData.detail) return responseData.detail;
  if (responseData.message) return responseData.message;
  const validationErrors = responseData.errors;
  if (!validationErrors || typeof validationErrors !== 'object') return null;
  const firstKey = Object.keys(validationErrors)[0];
  if (!firstKey || !Array.isArray(validationErrors[firstKey]) || validationErrors[firstKey].length === 0) return null;
  return validationErrors[firstKey][0] ?? null;
};

const passwordValidationRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,100}$/;

const validatePasswordForm = (form: PasswordFormState): string | null => {
  if (!form.currentPassword.trim()) return frontendI18n.t('Current password is required.');
  if (!form.newPassword.trim()) return frontendI18n.t('New password is required.');
  if (!form.confirmPassword.trim()) return frontendI18n.t('Please confirm your new password.');
  if (form.newPassword !== form.confirmPassword) return frontendI18n.t('New password and confirmation do not match.');
  if (form.currentPassword === form.newPassword)
    return frontendI18n.t('New password must be different from current password.');
  if (!passwordValidationRegex.test(form.newPassword))
    return frontendI18n.t(
      'Password must be 8-100 chars and include uppercase, lowercase, number, and special character.'
    );
  return null;
};

const PasswordInput = ({
  id,
  label,
  value,
  onChange,
  show,
  onToggleShow,
  disabled,
  placeholder,
  autoComplete,
  t,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  show: boolean;
  onToggleShow: () => void;
  disabled: boolean;
  placeholder: string;
  autoComplete: string;
  t: (key: string) => string;
}) => (
  <div className="space-y-1.5">
    <label htmlFor={id} className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark">
      {label}
    </label>
    <div className="relative">
      <Input
        id={id}
        type={show ? 'text' : 'password'}
        value={value}
        onChange={e => onChange(e.target.value)}
        autoComplete={autoComplete}
        disabled={disabled}
        className="pr-10"
        placeholder={placeholder}
      />
      <button
        type="button"
        onClick={onToggleShow}
        className="absolute right-2 top-1/2 -translate-y-1/2 p-1.5 text-content-muted hover:text-content dark:hover:text-content-dark rounded-md transition-colors"
        aria-label={show ? t('Hide password') : t('Show password')}
      >
        {show ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
      </button>
    </div>
  </div>
);

export const PasswordSection = () => {
  const { t } = useFrontendLanguage();
  const { showSuccess } = useToast();
  const changePasswordMutation = useChangePasswordMutation();
  const isMutating = changePasswordMutation.isPending;

  const [form, setForm] = useState<PasswordFormState>({
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
  });
  const [validationError, setValidationError] = useState<string | null>(null);
  const [showCurrent, setShowCurrent] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);

  const handleFieldChange = (field: keyof PasswordFormState, value: string) => {
    setForm(prev => ({ ...prev, [field]: value }));
    if (validationError) setValidationError(null);
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const error = validatePasswordForm(form);
    if (error) {
      setValidationError(error);
      return;
    }
    try {
      await changePasswordMutation.mutateAsync({
        currentPassword: form.currentPassword,
        newPassword: form.newPassword,
      });
      setForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
      setValidationError(null);
      showSuccess(t('Password updated'), t('Your password has been changed successfully.'));
    } catch (err) {
      const errorCode = getApiErrorCode(err);
      if (errorCode === 'CURRENT_PASSWORD_INCORRECT') {
        setValidationError(t('Current password is incorrect.'));
        return;
      }
      if (errorCode === 'PASSWORD_NOT_VALID') {
        setValidationError(
          t('Password must be 8-100 chars and include uppercase, lowercase, number, and special character.')
        );
        return;
      }
      const msg = getApiValidationMessage(err);
      setValidationError(msg ?? t('Failed to change password. Please try again.'));
      console.error('Failed to change password:', err);
    }
  };

  return (
    <section>
      <div className="mb-4">
        <h3 className="text-sm font-semibold text-content dark:text-content-dark">{t('Password')}</h3>
        <p className="text-xs text-content-muted dark:text-content-muted-dark mt-0.5">
          {t('Change your account password and keep your account secure.')}
        </p>
      </div>
      <form className="space-y-4 max-w-md" onSubmit={handleSubmit}>
        <PasswordInput
          id="current-password"
          label={t('Current Password')}
          value={form.currentPassword}
          onChange={v => handleFieldChange('currentPassword', v)}
          show={showCurrent}
          onToggleShow={() => setShowCurrent(v => !v)}
          disabled={isMutating}
          placeholder={t('Enter current password')}
          autoComplete="current-password"
          t={t}
        />
        <PasswordInput
          id="new-password"
          label={t('New Password')}
          value={form.newPassword}
          onChange={v => handleFieldChange('newPassword', v)}
          show={showNew}
          onToggleShow={() => setShowNew(v => !v)}
          disabled={isMutating}
          placeholder={t('Enter new password')}
          autoComplete="new-password"
          t={t}
        />
        <PasswordInput
          id="confirm-password"
          label={t('Confirm New Password')}
          value={form.confirmPassword}
          onChange={v => handleFieldChange('confirmPassword', v)}
          show={showConfirm}
          onToggleShow={() => setShowConfirm(v => !v)}
          disabled={isMutating}
          placeholder={t('Confirm new password')}
          autoComplete="new-password"
          t={t}
        />
        <p className="text-[11px] text-content-muted dark:text-content-muted-dark">
          {t('Password requirements: 8-100 chars, uppercase, lowercase, number, and special character.')}
        </p>
        {validationError && (
          <p className="text-xs text-red-600 dark:text-red-400" role="alert">
            {validationError}
          </p>
        )}
        <Button type="submit" size="sm" disabled={isMutating} isLoading={isMutating}>
          {t('Change password')}
        </Button>
      </form>
    </section>
  );
};
