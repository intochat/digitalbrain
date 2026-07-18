import { Check } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import type { PricingTier } from 'entities/pricing';
import { TIER_CONFIG } from 'shared/config/pricing/tierConfig';
import type { TierAction } from 'shared/lib/pricing/tierComparison';
import { Button } from 'shared/ui';

type TierDetails = (typeof TIER_CONFIG.tierDetails)[keyof typeof TIER_CONFIG.tierDetails];

interface PricingCardProps {
  tier: PricingTier;
  tierDetails: TierDetails;
  isAnnual: boolean;
  isHighlighted: boolean;
  onCtaClick: () => void;
  onFocus: () => void;
  isLoading?: boolean;
  tierAction?: TierAction;
  isCurrent?: boolean;
  onDowngrade?: () => void;
}

const CTA_CONFIG: Record<string, { variant: 'primary' | 'secondary'; labelKey: string }> = {
  upgrade: { variant: 'primary', labelKey: 'Upgrade' },
  downgrade: { variant: 'secondary', labelKey: 'Downgrade' },
  current: { variant: 'secondary', labelKey: 'Current Plan' },
};

export const PricingCard = ({
  tier,
  tierDetails,
  isAnnual,
  isHighlighted,
  onCtaClick,
  onFocus,
  isLoading,
  tierAction,
  isCurrent,
  onDowngrade,
}: PricingCardProps) => {
  const { t } = useFrontendLanguage();

  const price = isAnnual ? tier.price.annual : tier.price.monthly;
  const tierFeatures = tierDetails.features ?? TIER_CONFIG.fallbackFeatures;
  const normalizedTierName = tier.name.charAt(0).toUpperCase() + tier.name.slice(1).toLowerCase();
  const localizedTierName = t(normalizedTierName);
  const nameId = `tier-name-${tier.id}`;

  const cardBorder = isHighlighted
    ? 'border-2 border-secondary-500/45 dark:border-secondary-400/50'
    : 'border border-outline dark:border-outline-dark hover:border-outline-secondary dark:hover:border-outline-accent-dark';

  const ctaConfig = tierAction ? CTA_CONFIG[tierAction] : null;
  const ctaLabel = ctaConfig ? t(ctaConfig.labelKey) : t(tier.cta);
  const ctaVariant = ctaConfig?.variant ?? 'secondary';
  const ctaHandler = tierAction === 'downgrade' ? onDowngrade : onCtaClick;

  return (
    <article
      aria-labelledby={nameId}
      aria-current={isCurrent ? 'true' : undefined}
      className={`rounded-xl bg-surface dark:bg-surface-dark p-6 transition-all duration-200 flex flex-col h-full ${cardBorder}`}
    >
      <div className="mb-5">
        <div className="flex items-center gap-2 mb-0.5">
          <h3 id={nameId} className="text-base font-semibold text-content dark:text-content-dark">
            {localizedTierName}
          </h3>
          {isCurrent && (
            <span className="inline-flex items-center rounded-full bg-surface-accent dark:bg-surface-accent-dark border border-outline dark:border-outline-dark px-2 py-0.5 text-xs font-medium text-content-secondary dark:text-content-secondary-dark">
              {t('Current')}
            </span>
          )}
        </div>
        {tierDetails.subtitle && (
          <p className="text-sm text-content-muted dark:text-content-muted-dark">{t(tierDetails.subtitle)}</p>
        )}

        <div className="mt-4 flex items-end gap-1">
          <span className="text-2xl font-semibold text-content dark:text-content-dark">
            {new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(
              price
            )}
          </span>
          {price > 0 && (
            <span className="text-sm text-content-muted dark:text-content-muted-dark pb-0.5">
              /{isAnnual ? t('year') : t('month')}
            </span>
          )}
        </div>
        <p className="mt-1 text-xs text-content-muted dark:text-content-muted-dark">
          {tier.tokensPerMonthLimit?.toLocaleString() || t('Unlimited')} {t('tokens per month')}
        </p>
      </div>

      <ul className="mb-6 flex-grow space-y-2">
        {tierFeatures.map(feature => (
          <li
            key={`${tier.id}-${feature}`}
            className="flex items-start gap-2 text-sm text-content-secondary dark:text-content-secondary-dark"
          >
            <Check className="mt-0.5 h-3.5 w-3.5 shrink-0 text-secondary-600 dark:text-secondary-400" />
            <span>{t(feature)}</span>
          </li>
        ))}
      </ul>

      <Button
        variant={ctaVariant}
        size="md"
        className="w-full"
        onClick={ctaHandler}
        onFocus={onFocus}
        isLoading={isLoading}
        disabled={isCurrent}
        aria-label={`${ctaLabel} ${localizedTierName}`}
        aria-busy={isLoading || undefined}
      >
        {!isLoading && ctaLabel}
      </Button>
    </article>
  );
};
