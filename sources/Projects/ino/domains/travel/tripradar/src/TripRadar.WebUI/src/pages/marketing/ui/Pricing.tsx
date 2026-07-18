import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useDowngradeSubscriptionMutation } from 'entities/payment/api';
import { usePricingQuery } from 'entities/pricing';
import type { BillingPeriodType, UserTierType } from 'shared/api';
import { TIER_CONFIG } from 'shared/config/pricing/tierConfig';
import { ROUTES } from 'shared/config/routes';
import { useAuthStore } from 'shared/store/auth';
import { useTierInfo } from '../../profile/ui/billing/useTierInfo';
import { PricingCardGrid, PricingFAQ, FAQ_ITEMS, PricingHero } from '../components/pricing';

export const Pricing = () => {
  const { t } = useFrontendLanguage();
  const navigate = useNavigate();
  const { showError, showSuccess } = useToast();
  const { isAuthenticated } = useAuthStore();

  const [isAnnual, setIsAnnual] = useState(false);
  const [selectedTier, setSelectedTier] = useState<string | null>(null);
  const [pendingUpgradeTierId, setPendingUpgradeTierId] = useState<string | null>(null);

  const { data: pricingData, isLoading, error } = usePricingQuery();
  const tierInfo = useTierInfo({ enabled: isAuthenticated });
  const downgrade = useDowngradeSubscriptionMutation();

  const pricingTiers = useMemo(() => pricingData?.tiers || [], [pricingData]);
  const currentTierType = isAuthenticated ? (tierInfo.tierName?.toLowerCase() ?? null) : null;

  // Initialize billing toggle from subscription period
  useEffect(() => {
    if (tierInfo?.subscription?.billingPeriod) {
      setIsAnnual(tierInfo.subscription.billingPeriod === 'yearly');
    }
  }, [tierInfo?.subscription?.billingPeriod]);

  const handleCtaClick = (tierId: string) => {
    const normalizedId = tierId.toLowerCase() as keyof typeof TIER_CONFIG.tierDetails;
    const details = TIER_CONFIG.tierDetails[normalizedId];
    if (!details) return;

    if (!isAuthenticated) {
      navigate('/signup');
      return;
    }

    const { ctaAction } = details;

    if (ctaAction.action === 'navigate' && ctaAction.route) {
      navigate(ctaAction.route);
      return;
    }

    if (ctaAction.action === 'contact') {
      navigate('/contact');
      return;
    }

    const billingPeriodType: BillingPeriodType = isAnnual ? 'yearly' : 'monthly';
    setPendingUpgradeTierId(tierId);

    const params = new URLSearchParams({
      tier: normalizedId,
      billingPeriod: billingPeriodType,
    });

    navigate(`${ROUTES.SUBSCRIPTION_CHECKOUT}?${params.toString()}`);
    queueMicrotask(() => {
      setPendingUpgradeTierId(null);
    });
  };

  const handleDowngrade = (tierId: string) => {
    const billingPeriodType: BillingPeriodType = isAnnual ? 'yearly' : 'monthly';
    downgrade.mutate(
      {
        targetTierType: tierId as UserTierType,
        billingPeriodType,
      },
      {
        onSuccess: () => {
          showSuccess(t('Plan changed'), t('Your plan has been downgraded successfully'));
        },
        onError: err => {
          const message = err instanceof Error ? err.message : t('Failed to downgrade subscription');
          showError(t('Downgrade failed'), message);
        },
      }
    );
  };

  useEffect(() => {
    if (pricingTiers.length > 0) {
      setSelectedTier(prev => {
        if (prev) return prev;
        const featuredTierId = pricingTiers.find(tier => tier.id === TIER_CONFIG.featuredTierId)?.id;
        const fallbackTierId = pricingTiers[Math.floor(pricingTiers.length / 2)]?.id;
        return featuredTierId ?? fallbackTierId ?? null;
      });
    }
  }, [pricingTiers]);

  const discountedTiers = pricingTiers.filter(tier => tier.price.monthly > 0 && tier.price.annual > 0);
  const averageDiscount =
    discountedTiers.length > 0
      ? Math.round(
          discountedTiers.reduce((acc, tier) => acc + (1 - tier.price.annual / (tier.price.monthly * 12)) * 100, 0) /
            discountedTiers.length
        )
      : 0;

  const isAnyPending = downgrade.isPending;

  if (isLoading) {
    return (
      <div className="min-h-screen pt-14 bg-surface dark:bg-surface-dark pb-16">
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 pt-10 sm:pt-14 pb-6 text-center">
          <div className="h-8 w-3/4 max-w-[320px] mx-auto bg-outline/50 dark:bg-outline-dark/50 rounded-lg animate-pulse mb-6" />
          <div className="h-10 w-48 mx-auto bg-outline/50 dark:bg-outline-dark/50 rounded-full animate-pulse" />
        </div>
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-5 mt-6">
            {[1, 2, 3].map(i => (
              <div
                key={i}
                className="rounded-xl p-6 bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark animate-pulse flex flex-col gap-4"
              >
                <div className="h-5 w-1/2 bg-outline/50 dark:bg-outline-dark/50 rounded-lg" />
                <div className="h-8 w-1/3 bg-outline/50 dark:bg-outline-dark/50 rounded-lg" />
                <div className="h-3 w-3/4 bg-outline/50 dark:bg-outline-dark/50 rounded" />
                <div className="space-y-3 mt-2">
                  {[1, 2, 3, 4].map(j => (
                    <div key={j} className="h-3 w-full bg-outline/50 dark:bg-outline-dark/50 rounded" />
                  ))}
                </div>
                <div className="h-11 w-full bg-outline/50 dark:bg-outline-dark/50 rounded-xl mt-auto" />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="min-h-screen pt-14 flex items-center justify-center bg-surface dark:bg-surface-dark">
        <div className="flex flex-col items-center gap-3 text-center">
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Failed to load pricing')}
          </p>
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="text-sm text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
          >
            {t('Try again')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <>
      <a
        href="#pricing-content"
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:px-4 focus:py-2 focus:bg-button dark:focus:bg-button-dark focus:text-button-text dark:focus:text-button-text-dark focus:rounded-xl focus:shadow-lg focus:ring-2 focus:ring-outline dark:focus:ring-outline-dark focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark"
        aria-label={t('Skip to main content')}
      >
        {t('Skip to main content')}
      </a>

      <main
        id="pricing-content"
        className="min-h-screen pt-14 bg-surface dark:bg-surface-dark"
        role="main"
        aria-label={t('Pricing page')}
      >
        <PricingHero isAnnual={isAnnual} onToggle={setIsAnnual} averageDiscount={averageDiscount} />
        <PricingCardGrid
          tiers={pricingTiers}
          isAnnual={isAnnual}
          selectedTier={selectedTier}
          onCtaClick={handleCtaClick}
          onCardFocus={setSelectedTier}
          isCheckoutPending={isAnyPending}
          currentTierType={currentTierType}
          onDowngrade={isAuthenticated ? handleDowngrade : undefined}
          pendingTierId={pendingUpgradeTierId}
        />
        <PricingFAQ items={FAQ_ITEMS} supportEmail="support@tripradar.io" />
      </main>
    </>
  );
};
