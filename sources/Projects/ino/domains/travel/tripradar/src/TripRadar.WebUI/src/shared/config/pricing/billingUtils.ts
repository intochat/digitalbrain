import { TIER_CONFIG } from './tierConfig';

type TierKey = keyof typeof TIER_CONFIG.tierDetails;

/**
 * Returns features available in `currentTier` but missing in `targetTier`.
 * When `targetTier` is null or 'basic', returns all paid-tier features absent from basic.
 */
export const getFeatureDiff = (currentTier: string, targetTier: string | null): string[] => {
  const current = currentTier.toLowerCase() as TierKey;
  const target = (targetTier?.toLowerCase() ?? 'basic') as TierKey;

  const currentFeatures = TIER_CONFIG.tierDetails[current]?.features ?? [];
  const targetFeatures = TIER_CONFIG.tierDetails[target]?.features ?? [];

  return currentFeatures.filter(f => !(targetFeatures as readonly string[]).includes(f));
};
