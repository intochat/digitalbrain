import { useState, type FormEvent } from 'react';
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js';
import { loadStripe } from '@stripe/stripe-js';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useCreateSetupIntentMutation } from 'entities/payment/api';
import { env } from 'shared/config';

interface AddPaymentMethodFormProps {
  onAdded: () => Promise<void> | void;
  onCancel: () => void;
}

const stripePublishableKey = env.STRIPE_PUBLISHABLE_KEY.trim();
const stripePromise = stripePublishableKey ? loadStripe(stripePublishableKey) : null;

const cardElementOptions = {
  style: {
    base: {
      color: '#0f172a',
      fontFamily: '"Inter", ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif',
      fontSize: '16px',
      '::placeholder': {
        color: '#94a3b8',
      },
    },
    invalid: {
      color: '#dc2626',
    },
  },
  hidePostalCode: true,
};

const AddPaymentMethodFormInner = ({ onAdded, onCancel }: AddPaymentMethodFormProps) => {
  const { t } = useFrontendLanguage();
  const { showError, showSuccess } = useToast();
  const stripe = useStripe();
  const elements = useElements();
  const createSetupIntentMutation = useCreateSetupIntentMutation();

  const [cardholderName, setCardholderName] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const isSaving = createSetupIntentMutation.isPending;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setErrorMessage(null);

    if (!stripe || !elements) {
      setErrorMessage(t('Unable to initialize Stripe payment form.'));
      return;
    }

    const cardElement = elements.getElement(CardElement);
    if (!cardElement) {
      setErrorMessage(t('Card details are required.'));
      return;
    }

    try {
      const setupIntent = await createSetupIntentMutation.mutateAsync();
      const clientSecret = setupIntent.clientSecret;

      if (!clientSecret) {
        setErrorMessage(t('Unable to start card setup. Please try again.'));
        return;
      }

      const result = await stripe.confirmCardSetup(clientSecret, {
        payment_method: {
          card: cardElement,
          billing_details: {
            name: cardholderName.trim() || undefined,
          },
        },
      });

      if (result.error) {
        const message = result.error.message || t('Failed to save card. Please try again.');
        setErrorMessage(message);
        showError(t('Update failed'), message);
        return;
      }

      cardElement.clear();
      setCardholderName('');
      await onAdded();
      showSuccess(t('Success'), t('Payment method added successfully.'));
    } catch {
      const message = t('Failed to save card. Please try again.');
      setErrorMessage(message);
      showError(t('Update failed'), message);
    }
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="rounded-xl border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-3 sm:p-4 space-y-3"
    >
      <div>
        <label
          htmlFor="cardholder-name"
          className="block text-xs text-content-secondary dark:text-content-secondary-dark mb-1"
        >
          {t('Cardholder name')}
        </label>
        <input
          id="cardholder-name"
          type="text"
          value={cardholderName}
          onChange={event => setCardholderName(event.target.value)}
          placeholder={t('Optional')}
          className="w-full rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark px-3 py-2 text-sm text-content dark:text-content-dark focus:outline-none focus:ring-2 focus:ring-primary-500/20"
          autoComplete="cc-name"
        />
      </div>

      <div>
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark mb-1">{t('Card details')}</p>
        <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark px-3 py-3">
          <CardElement options={cardElementOptions} />
        </div>
      </div>

      {errorMessage && <p className="text-xs text-rose-600 dark:text-rose-300">{errorMessage}</p>}

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="submit"
          disabled={isSaving}
          className="inline-flex items-center rounded-lg bg-button dark:bg-button-dark px-3 py-2 text-xs font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {isSaving ? t('Saving card...') : t('Save card')}
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={isSaving}
          className="inline-flex items-center rounded-lg border border-outline dark:border-outline-dark px-3 py-2 text-xs text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {t('Cancel')}
        </button>
      </div>
    </form>
  );
};

export const AddPaymentMethodForm = ({ onAdded, onCancel }: AddPaymentMethodFormProps) => {
  const { t } = useFrontendLanguage();

  if (!stripePromise) {
    return (
      <div className="rounded-xl border border-amber-200 dark:border-amber-500/30 bg-amber-50 dark:bg-amber-500/10 p-3">
        <p className="text-sm font-semibold text-amber-800 dark:text-amber-300">
          {t('Stripe is not configured for this environment.')}
        </p>
        <p className="text-xs text-amber-700 dark:text-amber-400 mt-1">
          {t('Set VITE_STRIPE_PUBLISHABLE_KEY to enable card setup.')}
        </p>
      </div>
    );
  }

  return (
    <Elements stripe={stripePromise}>
      <AddPaymentMethodFormInner onAdded={onAdded} onCancel={onCancel} />
    </Elements>
  );
};
