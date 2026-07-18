import { useQuery } from '@tanstack/react-query';
import { apiClient, type PricesResponse, type PriceResponse } from 'shared/api';
import { TIER_CONFIG } from 'shared/config/pricing/tierConfig';
import { type PricingResponse, type PricingTier } from './pricingApi';

// Transform backend data to frontend format
const transformPricesToTiers = (prices: PriceResponse[]): PricingTier[] => {
  const tierMap = new Map<string, { monthly?: PriceResponse; annual?: PriceResponse }>();

  prices.forEach(price => {
    const key = price.tierName || 'unknown';
    if (!tierMap.has(key)) {
      tierMap.set(key, {});
    }
    const tier = tierMap.get(key)!;

    if (price.billingPeriodName?.toLowerCase() === 'monthly') {
      tier.monthly = price;
    } else if (price.billingPeriodName?.toLowerCase() === 'yearly') {
      tier.annual = price;
    }
  });

  const tiers = Array.from(tierMap.entries()).map(([tierName, periods]) => ({
    id: tierName.toLowerCase(),
    name: tierName,
    price: {
      monthly: periods.monthly?.amount || 0,
      annual: periods.annual?.amount || 0,
    },
    cta: TIER_CONFIG.defaultCta,
    tokensPerMonthLimit: periods.monthly?.tokensPerMonthLimit || periods.annual?.tokensPerMonthLimit,
  }));

  // Sort tiers by predefined order
  return tiers.sort((a, b) => {
    const orderA = (TIER_CONFIG.tierOrder as readonly string[]).indexOf(a.name);
    const orderB = (TIER_CONFIG.tierOrder as readonly string[]).indexOf(b.name);

    // If tier not in order config, put it at the end
    if (orderA === -1) return 1;
    if (orderB === -1) return -1;

    return orderA - orderB;
  });
};

const getPricing = async (): Promise<PricingResponse> => {
  const response: PricesResponse = await apiClient.get('/api/v1.0/payments/prices');
  return {
    tiers: transformPricesToTiers(response.prices || []),
  };
};

export const usePricingQuery = () => {
  return useQuery({
    queryKey: ['pricing'],
    queryFn: getPricing,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};
