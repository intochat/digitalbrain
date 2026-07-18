import { useFrontendLanguage } from 'app/providers';
import { Switch } from 'shared/ui';

export interface ToggleFieldProps {
  label: string;
  description?: string;
  value: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
  className?: string;
}

export const ToggleField = ({
  label,
  description,
  value,
  onChange,
  disabled = false,
  className = '',
}: ToggleFieldProps) => {
  const { t } = useFrontendLanguage();

  return (
    <div className={`flex items-center justify-between gap-3 ${className}`}>
      <div className="flex-1 min-w-0">
        <label className="block text-xs font-medium text-content dark:text-content-dark">{t(label)}</label>
        {description && (
          <p className="text-xs text-content-muted dark:text-content-muted-dark mt-0.5">{t(description)}</p>
        )}
      </div>

      <Switch checked={value} onChange={() => onChange(!value)} disabled={disabled} aria-label={t(label)} />
    </div>
  );
};
