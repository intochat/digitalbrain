import { useFrontendLanguage } from 'app/providers';

export interface TextFieldProps {
  label: string;
  description?: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  required?: boolean;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
}

export const TextField = ({
  label,
  description,
  value,
  onChange,
  error,
  required = false,
  disabled = false,
  placeholder,
  className = '',
}: TextFieldProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div
      className={`bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark rounded-xl p-4 transition-all duration-200 hover:bg-surface-accent dark:hover:bg-surface-accent-dark ${className}`}
    >
      <div className="space-y-3">
        <label className="block text-sm font-medium text-content dark:text-content-dark leading-tight">
          {t(label)}
          {required ? <span className="text-red-500 ml-1">*</span> : null}
        </label>

        {description ? (
          <p className="text-xs text-content-secondary dark:text-content-secondary-dark leading-relaxed">
            {t(description)}
          </p>
        ) : null}

        <input
          type="text"
          value={value}
          onChange={event => onChange(event.target.value)}
          disabled={disabled}
          placeholder={placeholder ? t(placeholder) : undefined}
          className={`
            w-full px-4 py-3 min-h-[44px]
            border rounded-xl text-sm
            focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark
            text-content dark:text-content-dark
            bg-surface-accent dark:bg-surface-accent-dark
            hover:bg-surface dark:hover:bg-surface-accent-dark-hover
            disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-surface-accent dark:disabled:hover:bg-surface-accent-dark
            transition-all duration-200 touch-manipulation
            ${
              error
                ? 'border-red-500 focus:border-red-500 focus:ring-red-500/20'
                : 'border-outline-secondary dark:border-outline-secondary-dark focus:border-primary-500 dark:focus:border-primary-400 hover:border-outline dark:hover:border-outline-dark'
            }
          `}
        />

        {error ? <p className="text-xs text-red-500 mt-2 leading-relaxed">{error}</p> : null}
      </div>
    </div>
  );
};
