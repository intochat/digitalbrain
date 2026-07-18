import { Bot, Zap, MessageCircle, PiggyBank } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';

const benefits = [
  {
    title: 'AI-Powered Planning',
    description: 'Advanced AI understands your preferences and creates personalized itineraries in seconds.',
    icon: Bot,
  },
  {
    title: 'Instant Results',
    description: 'Get complete travel plans instantly instead of spending hours researching and planning.',
    icon: Zap,
  },
  {
    title: 'Telegram Integration',
    description: 'Plan your trips directly in Telegram - no need to download another app or switch platforms.',
    icon: MessageCircle,
  },
  {
    title: 'Budget Optimization',
    description: 'Smart recommendations that fit your budget while maximizing your travel experience.',
    icon: PiggyBank,
  },
];

export const BenefitsSection = () => {
  const { t } = useFrontendLanguage();

  return (
    <section
      className="py-20 sm:py-24 px-4 sm:px-6 lg:px-8 bg-surface dark:bg-surface-dark"
      aria-labelledby="features-heading"
    >
      <div className="max-w-3xl mx-auto">
        <header className="text-center mb-12 sm:mb-14">
          <h2
            id="features-heading"
            className="text-2xl sm:text-3xl font-semibold tracking-tight text-content dark:text-content-dark mb-3"
          >
            {t('Why choose TripRadar?')}
          </h2>
          <p className="text-sm sm:text-base text-content-muted dark:text-content-muted-dark max-w-lg mx-auto">
            {t('Everything you need to plan the perfect trip, powered by AI')}
          </p>
        </header>

        <div
          className="grid grid-cols-1 sm:grid-cols-2 gap-4"
          role="list"
          aria-label={t('TripRadar key features and benefits')}
        >
          {benefits.map((benefit, index) => {
            const Icon = benefit.icon;
            return (
              <article
                key={index}
                className="rounded-xl border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-5 transition-colors hover:bg-surface-accent/50 dark:hover:bg-surface-accent-dark/50"
                role="listitem"
              >
                <div className="mb-3 flex h-8 w-8 items-center justify-center rounded-lg bg-surface-accent dark:bg-surface-accent-dark">
                  <Icon
                    className="h-4 w-4 text-content-secondary dark:text-content-secondary-dark"
                    aria-hidden="true"
                  />
                </div>
                <h3 className="text-sm font-medium text-content dark:text-content-dark mb-1">{t(benefit.title)}</h3>
                <p className="text-sm text-content-muted dark:text-content-muted-dark leading-relaxed">
                  {t(benefit.description)}
                </p>
              </article>
            );
          })}
        </div>
      </div>
    </section>
  );
};
