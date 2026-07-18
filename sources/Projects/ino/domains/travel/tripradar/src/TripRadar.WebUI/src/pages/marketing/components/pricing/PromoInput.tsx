import { Tag } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Input } from 'shared/ui';

interface PromoInputProps {
  promoCode: string;
  onPromoChange: (code: string) => void;
  isOpen: boolean;
  onToggleOpen: () => void;
  isDisabled: boolean;
  error?: string;
}

export const PromoInput = ({ promoCode, onPromoChange, isOpen, onToggleOpen, isDisabled, error }: PromoInputProps) => {
  const { t } = useFrontendLanguage();

  if (!isOpen) {
    return (
      <button
        type="button"
        onClick={onToggleOpen}
        className="text-sm text-content-secondary dark:text-content-secondary-dark hover:text-content dark:hover:text-content-dark transition-colors inline-flex items-center gap-1.5"
      >
        <Tag className="w-3.5 h-3.5" />
        {t('Have a promo code?')}
      </button>
    );
  }

  return (
    <div className="w-full max-w-[260px]">
      <div className="relative animate-in fade-in slide-in-from-top-2">
        <label htmlFor="promo-input" className="sr-only">
          {t('Promo code')}
        </label>
        <Tag className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-content-muted dark:text-content-muted-dark pointer-events-none" />
        <Input
          id="promo-input"
          type="text"
          value={promoCode}
          onChange={e => onPromoChange(e.target.value)}
          placeholder={t('Enter code')}
          disabled={isDisabled}
          error={!!error}
          aria-describedby={error ? 'promo-error' : undefined}
          className="pl-9 font-medium"
        />
      </div>
      {error && (
        <span id="promo-error" role="alert" className="mt-1 block text-xs text-red-600 dark:text-red-400">
          {error}
        </span>
      )}
    </div>
  );
};
