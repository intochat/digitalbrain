import { ROUTES } from 'shared/config/routes';
import type { FooterConfig } from './types';

/**
 * Footer configuration with legal and support link groups
 */
export const footerConfig: FooterConfig = {
  legalLinks: [
    { label: 'Privacy Policy', href: ROUTES.PRIVACY },
    { label: 'Cookies Policy', href: ROUTES.COOKIES },
    { label: 'Terms of Service', href: ROUTES.TERMS },
  ],
  supportLinks: [
    { label: 'Help Center', href: ROUTES.HELP },
    { label: 'Feedback', href: ROUTES.FEEDBACK },
    { label: 'Changelog', href: ROUTES.CHANGELOG },
  ],
  companyInfo: {
    name: 'Trip Radar',
  },
};
