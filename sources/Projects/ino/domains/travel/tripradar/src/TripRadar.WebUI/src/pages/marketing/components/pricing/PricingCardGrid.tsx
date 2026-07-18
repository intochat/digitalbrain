import type { PricingTier } from 'entities/pricing';
import { TIER_CONFIG } from 'shared/config/pricing/tierConfig';
import { getTierAction } from 'shared/lib/pricing/tierComparison';
import { PricingCard } from './PricingCard';

type TierDetails = (typeof TIER_CONFIG.tierDetails)[keyof typeof TIER_CONFIG.tierDetails];

const getTierDetails = (tierId: string): TierDetails | null => {
  const normalizedTierId = tierId.toLowerCase();
  if (normalizedTierId in TIER_CONFIG.tierDetails) {
    return TIER_CONFIG.tierDetails[normalizedTierId as keyof typeof TIER_CONFIG.tierDetails];
  }
  return null;
};

interface PricingCardGridProps {
  tiers: PricingTier[];
  isAnnual: boolean;
  selectedTier: string | null;
  onCtaClick: (tierId: string) => void;
  onCardFocus: (tierId: string) => void;
  isCheckoutPending: boolean;
  currentTierType?: string | null;
  onDowngrade?: (tierId: string) => void;
  pendingTierId?: string | null;
}

export const PricingCardGrid = ({
  tiers,
  isAnnual,
  selectedTier,
  onCtaClick,
  onCardFocus,
  isCheckoutPending,
  currentTierType,
  onDowngrade,
  pendingTierId,
}: PricingCardGridProps) => {
  return (
    <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 pb-16">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-5 mt-6 sm:max-w-md sm:mx-auto md:max-w-none">
        {tiers.map(tier => {
          const tierDetails = getTierDetails(tier.id);
          if (!tierDetails) return null;

          const tierAction = getTierAction(currentTierType ?? null, tier.id);
          const isCurrent = tierAction === 'current';
          const isFeatured = tier.id === TIER_CONFIG.featuredTierId;
          const isHighlighted = isCurrent || isFeatured || tier.id === selectedTier;
          const isPending = pendingTierId === tier.id;

          return (
            <PricingCard
              key={tier.id}
              tier={tier}
              tierDetails={tierDetails}
              isAnnual={isAnnual}
              isHighlighted={isHighlighted}
              onCtaClick={() => onCtaClick(tier.id)}
              onFocus={() => onCardFocus(tier.id)}
              isLoading={isPending || (isCheckoutPending && tier.id === selectedTier)}
              tierAction={tierAction}
              isCurrent={isCurrent}
              onDowngrade={onDowngrade ? () => onDowngrade(tier.id) : undefined}
            />
          );
        })}
      </div>
    </div>
  );
};
