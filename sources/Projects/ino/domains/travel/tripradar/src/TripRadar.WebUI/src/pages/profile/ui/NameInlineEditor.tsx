import { useState, useEffect } from 'react';
import { Check, X, Edit2, Loader2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

interface NameInlineEditorProps {
  firstName: string;
  lastName: string;
  onSave: (firstName: string, lastName: string) => Promise<void> | void;
  onCancel?: () => void;
  isLoading?: boolean;
  disabled?: boolean;
  className?: string;
  required?: boolean;
  onEditingChange?: (isEditing: boolean) => void;
}

export const NameInlineEditor = ({
  firstName,
  lastName,
  onSave,
  onCancel,
  isLoading = false,
  disabled = false,
  className = '',
  required = false,
  onEditingChange,
}: NameInlineEditorProps) => {
  const { t } = useFrontendLanguage();
  const [isEditing, setIsEditing] = useState(false);
  const [editFirstName, setEditFirstName] = useState(firstName);
  const [editLastName, setEditLastName] = useState(lastName);
  const [isSaving, setIsSaving] = useState(false);
  const [validationError, setValidationError] = useState<string>('');

  // Update edit values when props change
  useEffect(() => {
    setEditFirstName(firstName);
    setEditLastName(lastName);
  }, [firstName, lastName]);

  const displayName = firstName && lastName ? `${firstName} ${lastName}` : firstName || lastName || t('Not set');

  const handleEdit = () => {
    if (disabled || isLoading) return;
    setIsEditing(true);
    setEditFirstName(firstName);
    setEditLastName(lastName);
    onEditingChange?.(true);
  };

  const validateNames = (first: string, last: string): string => {
    const trimmedFirst = first.trim();
    const trimmedLast = last.trim();

    // Basic required field validation - at least one name is required
    if (required && !trimmedFirst && !trimmedLast) {
      return t('At least one name is required');
    }

    return '';
  };

  const handleSave = async () => {
    const trimmedFirst = editFirstName.trim();
    const trimmedLast = editLastName.trim();

    // Check if values actually changed
    if (trimmedFirst === firstName.trim() && trimmedLast === lastName.trim()) {
      handleCancel();
      return;
    }

    // Validate names
    const error = validateNames(editFirstName, editLastName);
    if (error) {
      setValidationError(error);
      return;
    }

    setValidationError('');
    setIsSaving(true);
    try {
      await onSave(trimmedFirst, trimmedLast);
      setIsEditing(false);
      onEditingChange?.(false);
    } catch (error) {
      // Error handling is done by the parent component via toast notifications
      console.error('Save failed:', error);
    } finally {
      setIsSaving(false);
    }
  };

  const handleCancel = () => {
    setIsEditing(false);
    setEditFirstName(firstName);
    setEditLastName(lastName);
    setValidationError('');
    onEditingChange?.(false);
    onCancel?.();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSave();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      handleCancel();
    }
  };

  const inputClassName = [
    'px-3 py-2 text-sm border rounded-lg focus:outline-none focus:ring-2',
    'text-content dark:text-content-dark bg-surface dark:bg-surface-dark transition-colors',
    validationError
      ? 'border-red-500 dark:border-red-400 focus:ring-red-500/20'
      : 'border-outline/60 dark:border-outline-dark/60 focus:ring-content/10 focus:border-content dark:focus:border-content-dark',
  ].join(' ');

  if (isEditing) {
    return (
      <div className={`space-y-2 ${className}`}>
        <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark">
          {t('Full Name')}
        </label>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          <input
            type="text"
            value={editFirstName}
            onChange={e => {
              setEditFirstName(e.target.value);
              if (validationError) setValidationError('');
            }}
            onKeyDown={handleKeyDown}
            placeholder={t('First name')}
            className={inputClassName}
            autoFocus
            disabled={isSaving}
            aria-invalid={validationError ? 'true' : 'false'}
          />
          <input
            type="text"
            value={editLastName}
            onChange={e => {
              setEditLastName(e.target.value);
              if (validationError) setValidationError('');
            }}
            onKeyDown={handleKeyDown}
            placeholder={t('Last name')}
            className={inputClassName}
            disabled={isSaving}
            aria-invalid={validationError ? 'true' : 'false'}
          />
        </div>
        <div className="flex items-center gap-1.5">
          <button
            onClick={handleSave}
            disabled={isSaving}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isSaving ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                {t('Saving...')}
              </>
            ) : (
              <>
                <Check className="h-3.5 w-3.5" />
                {t('Save')}
              </>
            )}
          </button>
          <button
            onClick={handleCancel}
            disabled={isSaving}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium border border-outline/60 dark:border-outline-dark/60 text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <X className="h-3.5 w-3.5" />
            {t('Cancel')}
          </button>
        </div>
        {validationError && (
          <p className="text-xs text-red-500 dark:text-red-400" role="alert" aria-live="polite">
            {validationError}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className={`group ${className}`}>
      <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-0.5">
        {t('Full Name')}
      </label>
      <button
        onClick={handleEdit}
        disabled={disabled || isLoading}
        className="w-full flex items-center justify-between gap-2 px-3 py-2 -mx-3 rounded-lg hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-content/10"
        aria-label={t('Edit name')}
      >
        <span className="text-sm text-content dark:text-content-dark">{displayName}</span>
        <span className="flex-shrink-0 text-content-muted/60 group-hover:text-content-muted transition-colors">
          {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Edit2 className="h-3.5 w-3.5" />}
        </span>
      </button>
    </div>
  );
};
