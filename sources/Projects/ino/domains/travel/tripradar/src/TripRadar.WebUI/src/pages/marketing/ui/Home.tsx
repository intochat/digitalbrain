import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { DemoSection, HeroSection } from '../components/home';

export const Home = () => {
  const location = useLocation();
  const { t } = useFrontendLanguage();

  useEffect(() => {
    if (location.hash) {
      const element = document.querySelector(location.hash);
      if (element) {
        setTimeout(() => {
          element.scrollIntoView({ behavior: 'smooth' });
        }, 100);
      }
    }
  }, [location.hash]);

  return (
    <>
      {/* Skip link for keyboard navigation */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:px-4 focus:py-2 focus:bg-button dark:focus:bg-button-dark focus:text-button-text dark:focus:text-button-text-dark focus:rounded-xl focus:shadow-lg focus:ring-2 focus:ring-content/10 focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark"
        aria-label={t('Skip to main content')}
      >
        {t('Skip to main content')}
      </a>

      <main
        id="main-content"
        className="min-h-screen bg-surface dark:bg-surface-dark transition-colors duration-300"
        role="main"
        aria-label={t('TripRadar home page')}
      >
        <section id="hero" aria-labelledby="hero-heading">
          <HeroSection />
        </section>
        <section id="demo" aria-label={t('Product demo')}>
          <DemoSection />
        </section>
      </main>

      {/* Live region for dynamic announcements */}
      <div id="live-region" aria-live="polite" aria-atomic="true" className="sr-only" role="status" />
    </>
  );
};
