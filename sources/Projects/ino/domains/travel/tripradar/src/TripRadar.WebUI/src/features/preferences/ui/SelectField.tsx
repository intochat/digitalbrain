import { useFrontendLanguage } from 'app/providers';
import { Dropdown } from 'shared/ui';
import type { DropdownOption } from 'shared/ui';

export interface SelectFieldOption {
  value: string | number;
  label: string;
  icon?: string;
  searchText?: string;
  isLabelTranslated?: boolean;
}

export interface SelectFieldProps {
  label: string;
  description?: string;
  value: string | number;
  options: SelectFieldOption[];
  onChange: (value: string | number) => void;
  error?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  searchable?: boolean;
  searchPlaceholder?: string;
  noResultsText?: string;
}

export const SelectField = ({
  label,
  description,
  value,
  options,
  onChange,
  error,
  required = false,
  disabled = false,
  className = '',
  searchable,
  searchPlaceholder,
  noResultsText,
}: SelectFieldProps) => {
  const { t } = useFrontendLanguage();

  const translatedOptions: DropdownOption<string | number>[] = options.map(option => ({
    value: option.value,
    label: option.isLabelTranslated ? option.label : t(option.label) || String(option.value),
    icon: option.icon,
    searchText: option.searchText,
  }));

  const resolvedSearchable = searchable ?? translatedOptions.some(option => Boolean(option.icon || option.searchText));

  return (
    <div className={`space-y-1.5 ${className}`}>
      <label className="block text-xs font-medium text-content dark:text-content-dark">
        {t(label)}
        {required && <span className="text-red-500 ml-0.5">*</span>}
      </label>

      {description && <p className="text-xs text-content-muted dark:text-content-muted-dark">{t(description)}</p>}

      <Dropdown
        value={value}
        options={translatedOptions}
        onChange={onChange}
        disabled={disabled}
        searchable={resolvedSearchable}
        searchPlaceholder={searchPlaceholder ? t(searchPlaceholder) : undefined}
        noResultsText={noResultsText ? t(noResultsText) : undefined}
        aria-label={t(label)}
      />

      {error && <p className="text-xs text-red-500">{error}</p>}
    </div>
  );
};
