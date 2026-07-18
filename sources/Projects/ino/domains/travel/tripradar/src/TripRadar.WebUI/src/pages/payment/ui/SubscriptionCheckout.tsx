import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Elements, PaymentElement, useElements, useStripe } from '@stripe/react-stripe-js';
import { loadStripe, type Appearance } from '@stripe/stripe-js';
import { ArrowLeft, Check, Loader2, ShieldCheck, Tag } from 'lucide-react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useCreateCheckoutMutation, type SubscriptionCheckoutResponse } from 'entities/payment/api';
import { usePricingQuery } from 'entities/pricing';
import { useProfileQuery } from 'entities/user/api';
import type { BillingPeriodType, CreateSubscriptionCheckoutRequest, UserTierType } from 'shared/api';
import { env } from 'shared/config';
import { TIER_CONFIG } from 'shared/config/pricing/tierConfig';
import { ROUTES } from 'shared/config/routes';
import { cn } from 'shared/lib/utils';
import { Button, Input } from 'shared/ui';

type CheckoutTierId = 'essential' | 'advanced';
type CheckoutBillingPeriod = 'monthly' | 'yearly';

const SUPPORTED_TIERS: CheckoutTierId[] = ['essential', 'advanced'];
const stripePublishableKey = env.STRIPE_PUBLISHABLE_KEY.trim();
const stripePromise = stripePublishableKey ? loadStripe(stripePublishableKey) : null;

const stripeAppearance: Appearance = {
  theme: 'flat',
  variables: {
    colorPrimary: '#111111',
    colorText: '#111111',
    colorTextPlaceholder: '#6b7280',
    colorBackground: '#ffffff',
    colorDanger: '#dc2626',
    borderRadius: '18px',
    fontFamily: '"Inter", ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    spacingUnit: '4px',
  },
  rules: {
    '.Tab': {
      border: '1px solid #e5e7eb',
      boxShadow: 'none',
    },
    '.Tab:hover': {
      color: '#111111',
    },
    '.Tab--selected': {
      borderColor: '#111111',
      backgroundColor: '#f8fafc',
    },
    '.Input': {
      borderColor: '#e5e7eb',
      boxShadow: 'none',
    },
    '.Input:focus': {
      borderColor: '#111111',
      boxShadow: '0 0 0 1px #111111',
    },
    '.Block': {
      borderColor: '#e5e7eb',
      boxShadow: 'none',
    },
  },
};

const apiTierMap: Record<CheckoutTierId, UserTierType> = {
  essential: 'Essential' as UserTierType,
  advanced: 'Advanced' as UserTierType,
};

const apiBillingPeriodMap: Record<CheckoutBillingPeriod, BillingPeriodType> = {
  monthly: 'Monthly' as BillingPeriodType,
  yearly: 'Yearly' as BillingPeriodType,
};

const parseTier = (value: string | null): CheckoutTierId | null => {
  if (!value) return null;
  const normalized = value.trim().toLowerCase() as CheckoutTierId;
  return SUPPORTED_TIERS.includes(normalized) ? normalized : null;
};

const parseBillingPeriod = (value: string | null): CheckoutBillingPeriod => {
  return value?.trim().toLowerCase() === 'yearly' ? 'yearly' : 'monthly';
};

const formatMoney = (amount: number, currency: string) =>
  new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: currency.toUpperCase(),
    maximumFractionDigits: amount % 1 === 0 ? 0 : 2,
  }).format(amount);

const buildCheckoutRequest = (
  tier: CheckoutTierId,
  billingPeriod: CheckoutBillingPeriod,
  promoCode?: string
): CreateSubscriptionCheckoutRequest => ({
  targetTierType: apiTierMap[tier],
  billingPeriodType: apiBillingPeriodMap[billingPeriod],
  promoCode: promoCode?.trim() || undefined,
});

interface CheckoutPaymentFormProps {
  email: string;
}

const CheckoutPaymentForm = ({ email }: CheckoutPaymentFormProps) => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const stripe = useStripe();
  const elements = useElements();
  const [isConfirming, setIsConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (!stripe || !elements || isConfirming) {
      return;
    }

    setIsConfirming(true);
    setError(null);

    const submission = await elements.submit();
    if (submission.error) {
      setError(submission.error.message ?? t('Payment details are incomplete.'));
      setIsConfirming(false);
      return;
    }

    const result = await stripe.confirmPayment({
      elements,
      redirect: 'if_required',
      confirmParams: {
        return_url: `${window.location.origin}${ROUTES.SUBSCRIPTION_SUCCESS}`,
      },
    });

    if (result.error) {
      setError(result.error.message ?? t('Unable to confirm payment right now.'));
      setIsConfirming(false);
      return;
    }

    if (!result.paymentIntent || ['processing', 'succeeded'].includes(result.paymentIntent.status)) {
      navigate(ROUTES.SUBSCRIPTION_SUCCESS, { replace: true });
      return;
    }

    setError(t('Unable to confirm payment right now.'));
    setIsConfirming(false);
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
        <p className="mb-3 text-sm font-semibold text-slate-900">{t('Contact details')}</p>
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">{email}</div>
      </div>

      <div className="rounded-[28px] border border-slate-200 bg-white p-6 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
        <p className="mb-5 text-sm font-semibold text-slate-900">{t('Payment method')}</p>
        <PaymentElement />
      </div>

      {error && <p className="text-sm text-rose-600">{error}</p>}

      <Button
        type="submit"
        className="h-14 w-full rounded-2xl bg-slate-950 text-base font-semibold text-white hover:bg-slate-800"
        disabled={!stripe || isConfirming}
      >
        {isConfirming ? (
          <span className="inline-flex items-center gap-2">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t('Processing payment...')}
          </span>
        ) : (
          t('Complete subscription')
        )}
      </Button>

      <p className="text-xs leading-6 text-slate-500">
        {t('By continuing, you agree to the')}{' '}
        <Link to={ROUTES.TERMS} className="text-slate-900 underline">
          {t('Terms')}
        </Link>{' '}
        {t('and')}{' '}
        <Link to={ROUTES.PRIVACY} className="text-slate-900 underline">
          {t('Privacy Policy')}
        </Link>
        .
      </p>
    </form>
  );
};

export const SubscriptionCheckout = () => {
  const { language, t } = useFrontendLanguage();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialTier = parseTier(searchParams.get('tier'));
  const { data: pricingData } = usePricingQuery();
  const { data: profile } = useProfileQuery();
  const createCheckout = useCreateCheckoutMutation();

  const [selectedTier] = useState<CheckoutTierId | null>(initialTier);
  const [billingPeriodType, setBillingPeriodType] = useState<CheckoutBillingPeriod>(
    parseBillingPeriod(searchParams.get('billingPeriod'))
  );
  const [promoCode, setPromoCode] = useState(searchParams.get('promoCode') ?? '');
  const [appliedPromoCode, setAppliedPromoCode] = useState(searchParams.get('promoCode') ?? '');
  const [checkoutIntent, setCheckoutIntent] = useState<SubscriptionCheckoutResponse | null>(null);
  const [initializationError, setInitializationError] = useState<string | null>(null);
  const [promoError, setPromoError] = useState<string | null>(null);

  const copy =
    language === 'ru'
      ? {
          subscriptionLabel: 'Подписка TripRadar',
          stripeUnavailableTitle: 'Stripe не настроен для этого окружения.',
          stripeUnavailableBody: 'Укажите VITE_STRIPE_PUBLISHABLE_KEY, чтобы включить оплату картой.',
        }
      : {
          subscriptionLabel: 'TripRadar subscription',
          stripeUnavailableTitle: 'Stripe is not configured for this environment.',
          stripeUnavailableBody: 'Set VITE_STRIPE_PUBLISHABLE_KEY to enable card setup.',
        };

  useEffect(() => {
    if (!selectedTier) {
      navigate(ROUTES.PRICING, { replace: true });
    }
  }, [navigate, selectedTier]);

  useEffect(() => {
    if (!selectedTier) {
      return;
    }

    const nextParams = new URLSearchParams();
    nextParams.set('tier', selectedTier);
    nextParams.set('billingPeriod', billingPeriodType);
    if (appliedPromoCode.trim()) {
      nextParams.set('promoCode', appliedPromoCode.trim());
    }

    if (nextParams.toString() !== searchParams.toString()) {
      setSearchParams(nextParams, { replace: true });
    }
  }, [appliedPromoCode, billingPeriodType, searchParams, selectedTier, setSearchParams]);

  useEffect(() => {
    if (!selectedTier) {
      return;
    }

    setInitializationError(null);
    setPromoError(null);

    createCheckout.mutate(buildCheckoutRequest(selectedTier, billingPeriodType, appliedPromoCode), {
      onSuccess: response => {
        setCheckoutIntent(response);
      },
      onError: error => {
        const checkoutError = error as Error & { code?: string };
        const promoCodeMessages: Record<string, string> = {
          PROMO_CODE_NOT_FOUND: t('This promo code does not exist.'),
          PROMO_CODE_EXPIRED: t('This promo code has expired.'),
          PROMO_CODE_INACTIVE: t('This promo code is not active.'),
          PROMO_CODE_NOT_STARTED: t('This promo code is not active yet.'),
          PROMO_CODE_USAGE_LIMIT_EXCEEDED: t('This promo code has reached its usage limit.'),
          PROMO_CODE_ALREADY_USED_BY_USER: t('You have already used this promo code.'),
        };

        const promoMessage = checkoutError.code ? promoCodeMessages[checkoutError.code] : undefined;
        if (promoMessage) {
          setPromoError(promoMessage);
          return;
        }

        const message = checkoutError instanceof Error ? checkoutError.message : t('Checkout unavailable right now');
        setCheckoutIntent(null);
        setInitializationError(message);
      },
    });
    // createCheckout is intentionally excluded to keep request lifecycle tied to user state changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [appliedPromoCode, billingPeriodType, selectedTier, t]);

  const selectedTierData = useMemo(
    () => pricingData?.tiers.find(tier => tier.id === selectedTier) ?? null,
    [pricingData?.tiers, selectedTier]
  );

  const tierDetails = selectedTier ? TIER_CONFIG.tierDetails[selectedTier] : null;
  const hasPendingPromoChange = promoCode.trim() !== appliedPromoCode.trim();
  const checkoutCurrency = checkoutIntent?.currency || 'USD';
  const billingLabel = billingPeriodType === 'yearly' ? t('Billed yearly') : t('Billed monthly');
  const displayedPrice =
    checkoutIntent?.amountTotal ?? (selectedTierData ? selectedTierData.price[billingPeriodType] : 0);
  const stripeLocale = language === 'ru' ? 'ru' : 'en';
  const isInitialCheckoutLoading = createCheckout.isPending && !checkoutIntent && !initializationError;
  const isRefreshingCheckout = createCheckout.isPending && Boolean(checkoutIntent);

  if (!selectedTier || !tierDetails) {
    return null;
  }

  return (
    <main className="min-h-screen bg-[#050505] text-white">
      <div className="grid min-h-screen lg:grid-cols-[minmax(0,1.05fr)_minmax(480px,0.95fr)]">
        <section className="relative overflow-hidden bg-[#050505] px-6 py-8 sm:px-10 lg:px-14">
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(34,197,94,0.15),transparent_34%),radial-gradient(circle_at_bottom_right,rgba(59,130,246,0.1),transparent_36%)]" />
          <div className="relative mx-auto flex h-full w-full max-w-xl flex-col">
            <Link
              to={ROUTES.PRICING}
              className="inline-flex w-fit items-center gap-2 text-sm text-slate-400 transition-colors hover:text-white"
            >
              <ArrowLeft className="h-4 w-4" />
              {t('Back to pricing')}
            </Link>

            <div className="mt-12 space-y-6">
              <div className="space-y-3">
                <p className="text-sm uppercase tracking-[0.22em] text-emerald-300/80">{copy.subscriptionLabel}</p>
                <h1 className="text-4xl font-semibold tracking-tight sm:text-5xl">
                  {formatMoney(displayedPrice, checkoutCurrency)}
                  <span className="ml-3 text-lg font-medium text-slate-400">
                    {billingPeriodType === 'yearly' ? t('per year') : t('per month')}
                  </span>
                </h1>
                <p className="max-w-md text-sm leading-7 text-slate-400">{billingLabel}</p>
              </div>

              <div className="inline-flex rounded-2xl border border-white/10 bg-white/5 p-1">
                {(['monthly', 'yearly'] as const).map(period => (
                  <button
                    key={period}
                    type="button"
                    className={cn(
                      'min-w-[150px] rounded-[14px] px-4 py-3 text-sm font-medium transition-colors',
                      billingPeriodType === period ? 'bg-white text-slate-950' : 'text-slate-300 hover:text-white'
                    )}
                    onClick={() => setBillingPeriodType(period)}
                    disabled={createCheckout.isPending}
                  >
                    {period === 'yearly' ? t('Yearly') : t('Monthly')}
                  </button>
                ))}
              </div>

              <div className="w-full max-w-xs">
                <label htmlFor="checkout-promo" className="mb-2 block text-sm font-medium text-slate-300">
                  {t('Promo code')}
                </label>
                <div className="flex items-center gap-2">
                  <div className="relative flex-1">
                    <Tag className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
                    <Input
                      id="checkout-promo"
                      value={promoCode}
                      onChange={event => {
                        setPromoCode(event.target.value);
                        if (promoError) {
                          setPromoError(null);
                        }
                      }}
                      placeholder={t('Enter code')}
                      className="border-white/10 bg-white/5 pl-10 text-white placeholder:text-slate-500"
                    />
                  </div>
                  <Button
                    type="button"
                    className="rounded-2xl bg-white px-4 py-3 text-sm font-semibold text-slate-950 hover:bg-slate-200"
                    disabled={!hasPendingPromoChange || createCheckout.isPending}
                    onClick={() => setAppliedPromoCode(promoCode.trim())}
                  >
                    {t('Apply')}
                  </Button>
                </div>
                {promoError && <p className="mt-3 text-sm text-rose-400">{promoError}</p>}
              </div>

              <div className="rounded-[28px] border border-white/10 bg-white/[0.045] p-6 shadow-[0_24px_80px_-36px_rgba(0,0,0,0.9)] backdrop-blur">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <p className="text-lg font-semibold text-white">{t(selectedTierData?.name ?? selectedTier)}</p>
                    <p className="mt-2 text-sm leading-6 text-slate-400">{t(tierDetails.subtitle)}</p>
                  </div>
                  <p className="text-lg font-semibold text-white">{formatMoney(displayedPrice, checkoutCurrency)}</p>
                </div>

                <ul className="mt-6 space-y-3 text-sm text-slate-300">
                  {tierDetails.features.slice(0, 5).map(feature => (
                    <li key={feature} className="flex items-start gap-3">
                      <Check className="mt-0.5 h-4 w-4 shrink-0 text-emerald-300" />
                      <span>{t(feature)}</span>
                    </li>
                  ))}
                </ul>

                <div className="mt-8 space-y-3 border-t border-white/10 pt-6 text-sm">
                  <div className="flex items-center justify-between text-slate-300">
                    <span>{t('Subtotal')}</span>
                    <span>{formatMoney(checkoutIntent?.amountSubtotal ?? displayedPrice, checkoutCurrency)}</span>
                  </div>
                  <div className="flex items-center justify-between text-slate-300">
                    <span>{t('Discount')}</span>
                    <span>
                      {checkoutIntent?.amountDiscount
                        ? `- ${formatMoney(checkoutIntent.amountDiscount, checkoutCurrency)}`
                        : '—'}
                    </span>
                  </div>
                  <div className="flex items-center justify-between border-t border-white/10 pt-4 text-base font-semibold text-white">
                    <span>{t('Total due today')}</span>
                    <span>{formatMoney(displayedPrice, checkoutCurrency)}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-start gap-3 rounded-3xl border border-emerald-400/15 bg-emerald-400/5 p-4 text-sm text-slate-200">
                <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-emerald-300" />
                <p className="leading-6">{t('Your subscription renews automatically until you cancel.')}</p>
              </div>
            </div>
          </div>
        </section>

        <section className="bg-[#f6f7fb] px-6 py-8 sm:px-10 lg:px-12">
          <div className="relative mx-auto flex h-full w-full max-w-lg flex-col justify-center">
            {isInitialCheckoutLoading && (
              <div className="relative space-y-5">
                <div className="h-24 animate-pulse rounded-[28px] border border-slate-200 bg-white" />
                <div className="h-72 animate-pulse rounded-[28px] border border-slate-200 bg-white" />
                <div className="h-14 animate-pulse rounded-2xl bg-slate-200" />
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="rounded-3xl border border-slate-200 bg-white/92 px-5 py-4 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
                    <span className="inline-flex items-center gap-3 text-sm font-medium text-slate-700">
                      <Loader2 className="h-4 w-4 animate-spin text-slate-950" />
                      {t('Loading secure payment form...')}
                    </span>
                  </div>
                </div>
              </div>
            )}

            {!isInitialCheckoutLoading && initializationError && (
              <div className="rounded-[28px] border border-rose-200 bg-white p-8 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
                <p className="text-sm font-semibold text-slate-950">{t('Checkout unavailable right now')}</p>
                <p className="mt-3 text-sm leading-6 text-slate-600">{initializationError}</p>
                <Button
                  type="button"
                  variant="secondary"
                  className="mt-6 h-12 w-full rounded-2xl"
                  onClick={() => navigate(ROUTES.PRICING)}
                >
                  {t('Back to pricing')}
                </Button>
              </div>
            )}

            {!initializationError && checkoutIntent && stripePromise && (
              <Elements
                key={checkoutIntent.clientSecret}
                stripe={stripePromise}
                options={{
                  clientSecret: checkoutIntent.clientSecret,
                  appearance: stripeAppearance,
                  locale: stripeLocale,
                }}
              >
                <CheckoutPaymentForm email={profile?.email ?? ''} />
              </Elements>
            )}

            {!initializationError && checkoutIntent && !stripePromise && (
              <div className="rounded-[28px] border border-amber-200 bg-white p-8 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
                <p className="text-sm font-semibold text-slate-950">{copy.stripeUnavailableTitle}</p>
                <p className="mt-3 text-sm leading-6 text-slate-600">{copy.stripeUnavailableBody}</p>
                <Button
                  type="button"
                  variant="secondary"
                  className="mt-6 h-12 w-full rounded-2xl"
                  onClick={() => navigate(ROUTES.PRICING)}
                >
                  {t('Back to pricing')}
                </Button>
              </div>
            )}

            {isRefreshingCheckout && (
              <div className="absolute inset-0 flex items-center justify-center rounded-[32px] bg-slate-50/75 backdrop-blur-[2px]">
                <div className="rounded-3xl border border-slate-200 bg-white/95 px-5 py-4 shadow-[0_24px_60px_-28px_rgba(15,23,42,0.22)]">
                  <span className="inline-flex items-center gap-3 text-sm font-medium text-slate-700">
                    <Loader2 className="h-4 w-4 animate-spin text-slate-950" />
                    {t('Loading secure payment form...')}
                  </span>
                </div>
              </div>
            )}
          </div>
        </section>
      </div>
    </main>
  );
};
