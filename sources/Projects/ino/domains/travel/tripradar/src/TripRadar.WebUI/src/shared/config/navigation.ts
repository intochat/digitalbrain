import type { FrontendTranslationKey } from 'shared/i18n';
import { ROUTES } from './routes';

export interface NavigationItem {
  name: string;
  href: string;
  protected?: boolean;
  translationKey?: FrontendTranslationKey;
}

export const NAVIGATION: NavigationItem[] = [
  { name: 'Home', translationKey: 'navigation.home', href: '#hero' },
  { name: 'Pricing', translationKey: 'navigation.pricing', href: ROUTES.PRICING },
];
