export type TierAction = 'upgrade' | 'downgrade' | 'current' | 'default';

export const TIER_ORDER: Record<string, number> = { basic: 0, essential: 1, advanced: 2 };

export const getTierIndex = (tierId: string): number => TIER_ORDER[tierId.toLowerCase()] ?? -1;

export const getTierAction = (currentTierType: string | null, targetTierId: string): TierAction => {
  if (!currentTierType) return 'default';
  const currentIndex = getTierIndex(currentTierType);
  const targetIndex = getTierIndex(targetTierId);
  if (currentIndex < 0 || targetIndex < 0) return 'default';
  if (targetIndex === currentIndex) return 'current';
  return targetIndex > currentIndex ? 'upgrade' : 'downgrade';
};

export const getLostFeatures = (currentTierFeatures: string[], targetTierFeatures: string[]): string[] =>
  currentTierFeatures.filter(f => !targetTierFeatures.includes(f));
