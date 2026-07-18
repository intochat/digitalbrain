import { ChevronDown } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { Button } from 'shared/ui';
import type { FAQItem } from './types';

interface PricingFAQProps {
  items: FAQItem[];
  supportEmail: string;
}

export const PricingFAQ = ({ items, supportEmail }: PricingFAQProps) => {
  const { t } = useFrontendLanguage();

  return (
    <section className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 pb-16 mt-12" aria-labelledby="faq-heading">
      <h2 id="faq-heading" className="text-xl font-semibold text-content dark:text-content-dark mb-6 text-center">
        {t('Frequently asked questions')}
      </h2>

      <div className="space-y-2">
        {items.map((item, index) => (
          <details
            key={index}
            className="group rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark"
          >
            <summary className="flex cursor-pointer items-center justify-between gap-4 px-4 py-3.5 text-sm font-medium text-content dark:text-content-dark select-none [&::-webkit-details-marker]:hidden list-none">
              {t(item.question)}
              <ChevronDown className="h-4 w-4 shrink-0 text-content-muted dark:text-content-muted-dark transition-transform duration-200 group-open:rotate-180" />
            </summary>
            <div className="px-4 pb-4 text-sm text-content-secondary dark:text-content-secondary-dark leading-relaxed">
              {t(item.answer)}
            </div>
          </details>
        ))}
      </div>

      <div className="mt-8 text-center">
        <p className="text-sm text-content-muted dark:text-content-muted-dark mb-3">
          {t("Still have questions? We're here to help.")}
        </p>
        <Button variant="secondary" size="sm" onClick={() => window.open(`mailto:${supportEmail}`, '_self')}>
          {t('Contact Support')}
        </Button>
      </div>
    </section>
  );
};
