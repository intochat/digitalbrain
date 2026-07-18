import { useFrontendLanguage } from 'app/providers';
import { BillingToggle } from './BillingToggle';

interface PricingHeroProps {
  isAnnual: boolean;
  onToggle: (isAnnual: boolean) => void;
  averageDiscount: number;
}

export const PricingHero = ({ isAnnual, onToggle, averageDiscount }: PricingHeroProps) => {
  const { t } = useFrontendLanguage();

  return (
    <section className="bg-surface dark:bg-surface-dark" aria-labelledby="pricing-heading">
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 pt-10 sm:pt-14 pb-6 sm:pb-8">
        <div className="text-center">
          <h1
            id="pricing-heading"
            className="text-2xl sm:text-3xl font-semibold text-content dark:text-content-dark mb-6"
          >
            {t('Choose your plan')}
          </h1>

          <div className="flex flex-col items-center gap-4">
            <BillingToggle isAnnual={isAnnual} onToggle={onToggle} averageDiscount={averageDiscount} />
          </div>
        </div>
      </div>
    </section>
  );
};
