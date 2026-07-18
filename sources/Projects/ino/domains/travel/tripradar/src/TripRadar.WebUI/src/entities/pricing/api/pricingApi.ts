export interface PricingTier {
  id: string;
  name: string;
  price: {
    monthly: number;
    annual: number;
  };
  cta: string;
  tokensPerMonthLimit?: number;
}

export interface PricingResponse {
  tiers: PricingTier[];
}
