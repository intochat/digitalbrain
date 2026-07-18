export interface FooterLink {
  label: string;
  href: string;
}

/**
 * Footer configuration with two link groups: legal and support
 */
export interface FooterConfig {
  legalLinks: FooterLink[];
  supportLinks: FooterLink[];
  companyInfo: {
    name: string;
  };
}
