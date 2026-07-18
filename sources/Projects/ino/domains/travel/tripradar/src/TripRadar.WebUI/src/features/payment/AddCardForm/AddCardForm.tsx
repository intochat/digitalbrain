import { type FormEvent, useEffect, useState } from 'react';
import { CardElement, useElements, useStripe } from '@stripe/react-stripe-js';
import type { StripeCardElementChangeEvent } from '@stripe/stripe-js';
import { useQueryClient } from '@tanstack/react-query';
import { Loader2, RefreshCw } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useTheme } from 'app/providers/ThemeContext';
import { paymentKeys, useCreateSetupIntentMutation } from 'entities/payment/api';
import { StripeProvider } from 'shared/providers/StripeProvider';

interface AddCardFormProps {
  onSuccess?: () => void;
  onCancel?: () => void;
}

interface CardFormContentProps {
  clientSecret: string;
  onSuccess?: () => void;
  onCancel?: () => void;
}

const CardFormContent = ({ clientSecret, onSuccess, onCancel }: CardFormContentProps) => {
  const { t } = useFrontendLanguage();
  const { theme } = useTheme();
  const isDark = theme === 'dark';
  const stripe = useStripe();
  const elements = useElements();
  const queryClient = useQueryClient();
  const [isConfirming, setIsConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isCardComplete, setIsCardComplete] = useState(false);

  const handleCardChange = (event: StripeCardElementChangeEvent) => {
    setIsCardComplete(event.complete);
    if (event.error) {
      setError(event.error.message);
    } else {
      setError(null);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!stripe || !elements || isConfirming) return;

    const cardElement = elements.getElement(CardElement);
    if (!cardElement) return;

    setIsConfirming(true);
    setError(null);

    const result = await stripe.confirmCardSetup(clientSecret, {
      payment_method: { card: cardElement },
    });

    if (result.error) {
      setError(result.error.message ?? t('An error occurred while adding your card'));
      setIsConfirming(false);
    } else {
      await queryClient.invalidateQueries({ queryKey: paymentKeys.paymentMethods() });
      setIsConfirming(false);
      onSuccess?.();
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label className="block text-sm font-medium text-content dark:text-content-dark mb-1.5">
          {t('Card details')}
        </label>
        <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-3">
          <CardElement
            onChange={handleCardChange}
            options={{
              style: {
                base: {
                  fontSize: '16px',
                  color: isDark ? '#e5e7eb' : '#0f172a',
                  fontFamily: '"Inter", ui-sans-serif, system-ui, -apple-system, sans-serif',
                  '::placeholder': { color: isDark ? '#6b7280' : '#94a3b8' },
                },
                invalid: { color: '#dc2626' },
              },
            }}
          />
        </div>
      </div>

      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}

      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onCancel}
          disabled={isConfirming}
          className="px-4 py-2 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors disabled:opacity-50"
        >
          {t('Cancel')}
        </button>
        <button
          type="submit"
          disabled={isConfirming || !stripe || !isCardComplete}
          className="px-4 py-2 text-sm font-medium rounded-lg bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors disabled:opacity-50 flex items-center gap-2"
        >
          {isConfirming && <Loader2 className="h-4 w-4 animate-spin" />}
          {t('Add Card')}
        </button>
      </div>
    </form>
  );
};

export const AddCardForm = ({ onSuccess, onCancel }: AddCardFormProps) => {
  const { t } = useFrontendLanguage();
  const setupIntent = useCreateSetupIntentMutation();

  useEffect(() => {
    setupIntent.mutate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (setupIntent.data?.clientSecret) {
    return (
      <StripeProvider clientSecret={setupIntent.data.clientSecret}>
        <CardFormContent clientSecret={setupIntent.data.clientSecret} onSuccess={onSuccess} onCancel={onCancel} />
      </StripeProvider>
    );
  }

  if (setupIntent.isError) {
    return (
      <div className="space-y-4">
        <p className="text-sm text-red-600 dark:text-red-400">
          {setupIntent.error instanceof Error ? setupIntent.error.message : t('Failed to initialize card form')}
        </p>
        <div className="flex gap-3">
          <button
            type="button"
            onClick={() => setupIntent.mutate()}
            className="px-4 py-2 text-sm font-medium rounded-lg bg-button dark:bg-button-dark text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors flex items-center gap-2"
          >
            <RefreshCw className="h-4 w-4" />
            {t('Retry')}
          </button>
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
          >
            {t('Cancel')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-3">
        <div className="animate-pulse space-y-3">
          <div className="h-4 bg-outline dark:bg-outline-dark rounded w-1/3" />
          <div className="h-10 bg-outline dark:bg-outline-dark rounded" />
        </div>
      </div>
      <div className="flex justify-end">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm font-medium rounded-lg border border-outline dark:border-outline-dark text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors"
        >
          {t('Cancel')}
        </button>
      </div>
    </div>
  );
};
