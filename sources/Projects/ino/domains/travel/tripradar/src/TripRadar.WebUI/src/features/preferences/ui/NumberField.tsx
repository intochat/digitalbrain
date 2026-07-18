import { useId } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { Input } from 'shared/ui';

export interface NumberFieldProps {
  label: string;
  description?: string;
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  error?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
}

export const NumberField = ({
  label,
  description,
  value,
  onChange,
  min,
  max,
  step = 1,
  error,
  required = false,
  disabled = false,
  className = '',
}: NumberFieldProps) => {
  const { t } = useFrontendLanguage();
  const inputId = useId();

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = Number(event.target.value);
    if (min !== undefined && newValue < min) return;
    if (max !== undefined && newValue > max) return;
    onChange(newValue);
  };

  const handleBlur = (event: React.FocusEvent<HTMLInputElement>) => {
    const newValue = Number(event.target.value);
    if (min !== undefined && newValue < min) {
      onChange(min);
    } else if (max !== undefined && newValue > max) {
      onChange(max);
    }
  };

  return (
    <div className={`space-y-1.5 ${className}`}>
      <label htmlFor={inputId} className="block text-xs font-medium text-content dark:text-content-dark">
        {t(label)}
        {required && <span className="text-red-500 ml-0.5">*</span>}
      </label>

      {description && <p className="text-xs text-content-muted dark:text-content-muted-dark">{t(description)}</p>}

      <Input
        id={inputId}
        type="number"
        value={value}
        onChange={handleChange}
        onBlur={handleBlur}
        min={min}
        max={max}
        step={step}
        disabled={disabled}
        error={!!error}
      />

      {error && <p className="text-xs text-red-500">{error}</p>}
    </div>
  );
};
