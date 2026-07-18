import { useState, useEffect } from 'react';
import { Check, X, Edit2, Loader2 } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

interface InlineEditorProps {
  value: string;
  onSave: (value: string) => Promise<void> | void;
  onCancel?: () => void;
  placeholder?: string;
  label?: string;
  isLoading?: boolean;
  disabled?: boolean;
  className?: string;
  inputClassName?: string;
  required?: boolean;
  type?: 'text' | 'email' | 'tel';
  onEditingChange?: (isEditing: boolean) => void;
}

export const InlineEditor = ({
  value,
  onSave,
  onCancel,
  placeholder,
  label,
  isLoading = false,
  disabled = false,
  className = '',
  inputClassName = '',
  required = false,
  type = 'text',
  onEditingChange,
}: InlineEditorProps) => {
  const { t } = useFrontendLanguage();
  const [isEditing, setIsEditing] = useState(false);
  const [editValue, setEditValue] = useState(value);
  const [isSaving, setIsSaving] = useState(false);
  const [validationError, setValidationError] = useState<string>('');
  const resolvedPlaceholder = placeholder ?? t('Enter value...');

  // Update edit value when prop value changes
  useEffect(() => {
    setEditValue(value);
  }, [value]);

  const handleEdit = () => {
    if (disabled || isLoading) return;
    setIsEditing(true);
    setEditValue(value);
    onEditingChange?.(true);
  };

  const validateInput = (inputValue: string): string => {
    const trimmedValue = inputValue.trim();

    // Basic required field validation
    if (required && !trimmedValue) {
      return t('This field is required');
    }

    // Basic email validation if type is email
    if (type === 'email' && trimmedValue) {
      const emailRegex = /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i;
      if (!emailRegex.test(trimmedValue)) {
        return t('Please enter a valid email address');
      }
    }

    return '';
  };

  const handleSave = async () => {
    const trimmedValue = editValue.trim();

    if (trimmedValue === value.trim()) {
      handleCancel();
      return;
    }

    // Validate input
    const error = validateInput(editValue);
    if (error) {
      setValidationError(error);
      return;
    }

    setValidationError('');
    setIsSaving(true);
    try {
      await onSave(trimmedValue);
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
    setEditValue(value);
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

  if (isEditing) {
    return (
      <div className={`space-y-1.5 ${className}`}>
        {label && (
          <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark">
            {label}
          </label>
        )}
        <div className="flex items-center gap-1.5">
          <input
            type={type}
            value={editValue}
            onChange={e => {
              setEditValue(e.target.value);
              if (validationError) setValidationError('');
            }}
            onKeyDown={handleKeyDown}
            placeholder={resolvedPlaceholder}
            className={`flex-1 px-3 py-2 text-sm border rounded-lg focus:outline-none focus:ring-2 text-content dark:text-content-dark bg-surface dark:bg-surface-dark transition-colors ${
              validationError
                ? 'border-red-500 dark:border-red-400 focus:ring-red-500/20'
                : 'border-outline/60 dark:border-outline-dark/60 focus:ring-content/10 focus:border-content dark:focus:border-content-dark'
            } ${inputClassName}`}
            autoFocus
            disabled={isSaving}
            aria-invalid={validationError ? 'true' : 'false'}
            aria-describedby={validationError ? 'validation-error' : undefined}
          />
          <button
            onClick={handleSave}
            disabled={isSaving || (editValue.trim() === value.trim() && !validationError)}
            className="p-2 flex items-center justify-center text-green-600 dark:text-green-400 hover:bg-green-50 dark:hover:bg-green-500/10 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            title={t('Save changes')}
            aria-label={t('Save changes')}
          >
            {isSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
          </button>
          <button
            onClick={handleCancel}
            disabled={isSaving}
            className="p-2 flex items-center justify-center text-content-muted hover:text-content dark:hover:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            title={t('Cancel changes')}
            aria-label={t('Cancel changes')}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
        {validationError && (
          <p id="validation-error" className="text-xs text-red-500 dark:text-red-400" role="alert" aria-live="polite">
            {validationError}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className={`group ${className}`}>
      {label && (
        <label className="block text-xs font-medium text-content-secondary dark:text-content-secondary-dark mb-0.5">
          {label}
        </label>
      )}
      <button
        onClick={handleEdit}
        disabled={disabled || isLoading}
        className="w-full flex items-center justify-between gap-2 px-3 py-2 -mx-3 rounded-lg hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-content/10"
        aria-label={`${t('Edit')} ${label || ''}`}
      >
        <span
          className={`text-sm ${value ? 'text-content dark:text-content-dark' : 'text-content-muted dark:text-content-muted-dark'}`}
        >
          {value || resolvedPlaceholder}
        </span>
        <span className="flex-shrink-0 text-content-muted/60 group-hover:text-content-muted transition-colors">
          {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Edit2 className="h-3.5 w-3.5" />}
        </span>
      </button>
    </div>
  );
};
