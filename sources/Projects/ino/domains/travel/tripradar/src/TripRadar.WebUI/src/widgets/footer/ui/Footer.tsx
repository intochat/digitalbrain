import { memo, useMemo } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { footerConfig } from '../model/config';
import type { FooterLink } from '../model/types';

interface FooterProps {
  className?: string;
}

const AUTH_PAGES = ['/signin', '/signup'] as const;

const linkClassName =
  'flex min-h-[36px] items-center justify-center px-2 py-1 text-xs transition-colors duration-150 hover:text-content dark:hover:text-content-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-content/10 focus-visible:ring-offset-2 focus-visible:ring-offset-surface dark:focus-visible:ring-offset-surface-dark';

export const Footer = memo<FooterProps>(({ className }) => {
  const location = useLocation();
  const { t } = useFrontendLanguage();

  const isAuthPage = useMemo(() => {
    return AUTH_PAGES.includes(location.pathname as (typeof AUTH_PAGES)[number]);
  }, [location.pathname]);

  const footerClassName = useMemo(() => {
    return `border-t border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark py-3 md:py-4 ${isAuthPage ? 'hidden md:block' : ''} ${className || ''}`;
  }, [isAuthPage, className]);

  const renderLinks = (links: FooterLink[]) =>
    links.map(link => (
      <Link key={link.href} to={link.href} className={linkClassName}>
        {t(link.label)}
      </Link>
    ));

  return (
    <footer aria-label={t('Site footer')} className={footerClassName}>
      <div className="max-w-6xl mx-auto px-4 sm:px-6">
        <div className="flex flex-col items-center gap-2">
          {/* Navigation links */}
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 md:flex md:flex-row md:items-center md:gap-0 text-content-secondary dark:text-content-secondary-dark">
            <nav aria-label={t('Legal links')} className="contents md:flex md:items-center md:gap-1">
              {renderLinks(footerConfig.legalLinks)}
            </nav>

            <span
              aria-hidden="true"
              className="hidden md:block mx-2 text-content-muted dark:text-content-muted-dark select-none"
            >
              |
            </span>

            <nav aria-label={t('Support links')} className="contents md:flex md:items-center md:gap-1">
              {renderLinks(footerConfig.supportLinks)}
            </nav>
          </div>

          {/* Copyright */}
          <p className="text-xs text-content-muted dark:text-content-muted-dark">
            © {new Date().getFullYear()} {footerConfig.companyInfo.name}
          </p>
        </div>
      </div>
    </footer>
  );
});

Footer.displayName = 'Footer';
