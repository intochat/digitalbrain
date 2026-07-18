import { ROUTES } from 'shared/config/routes';

export interface TierCtaConfig {
  action: 'navigate' | 'checkout' | 'contact';
  route?: string;
}

export const TIER_CONFIG = {
  tierOrder: ['Basic', 'Essential', 'Advanced'],
  defaultCta: 'Get Started',
  featuredTierId: 'essential',
  fallbackFeatures: ['Core trip planning tools', 'Live pricing updates', 'Standard support'],
  tierDetails: {
    basic: {
      badge: null,
      subtitle: 'Good for starting',
      savingsLabel: 'Save up to 220 EUR per trip',
      features: [
        'No credit card required',
        '50 tokens every month',
        'Manual trip requests',
        'Save on hotels and flights with smart budget suggestions',
        'Discover unique places and restaurants',
      ],
      ctaAction: { action: 'navigate', route: ROUTES.SIGNUP } as TierCtaConfig,
    },
    essential: {
      badge: null,
      subtitle: 'For active travel planning',
      savingsLabel: 'Save up to 520 EUR per trip',
      features: [
        '500 tokens every month (10x of Basic)',
        'Includes all Basic features',
        'Access to all features',
        'Scheduled requests support',
        'Query history included',
        'No-trace mode included',
        'AI chat',
        'Deep search for max accuracy',
      ],
      ctaAction: { action: 'checkout' } as TierCtaConfig,
    },
    advanced: {
      badge: null,
      subtitle: 'Best value for frequent travelers and teams',
      savingsLabel: 'Save up to 980 EUR per trip',
      features: [
        '3000 tokens every month (60x of Basic)',
        'Includes all Essential features',
        'Best price per token for heavy usage',
        'Access to all features',
        'Early access to advanced AI features',
        'Priority support',
      ],
      ctaAction: { action: 'checkout' } as TierCtaConfig,
    },
  },
} as const;
