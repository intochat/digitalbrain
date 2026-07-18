import { useFrontendLanguage } from 'app/providers';
import type { HeroSectionProps } from './types';

export const HeroSection = ({ className }: HeroSectionProps = {}) => {
  const { t } = useFrontendLanguage();

  return (
    <section
      className={`bg-surface dark:bg-surface-dark min-h-screen flex items-center justify-center ${className || ''}`}
      aria-label={t("Hero section introducing TripRadar's travel planning platform")}
      role="banner"
    >
      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="space-y-6">
          <h1 className="text-center text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark" id="hero-headline">
            {t('Save money on every trip: find the best options in seconds')}
          </h1>

          <p
            className="text-center text-base sm:text-lg text-content-muted dark:text-content-muted-dark max-w-xl mx-auto leading-relaxed"
            aria-describedby="hero-headline"
            id="hero-description"
          >
            {t('TripRadar compares routes and prices fast, so you can book with lower total cost.')}
          </p>
        </div>
      </div>
    </section>
  );
};
