import { useFrontendLanguage } from 'app/providers';

const steps = [
  {
    title: 'Share trip goals in Telegram',
    description: 'Send destination, dates, budget, and preferences in one request.',
  },
  {
    title: 'Get a complete plan fast',
    description: 'TripRadar returns routes, costs, and options without manual tab-switching.',
  },
  {
    title: 'Track and improve each trip',
    description: 'Store history, compare options, and refine your future plans with less effort.',
  },
];

export const HowItWorksSection = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="min-h-screen flex flex-col justify-center px-4 sm:px-6 lg:px-8 bg-surface-accent dark:bg-surface-accent-dark">
      <div className="max-w-3xl mx-auto">
        <header className="text-center mb-12 sm:mb-14">
          <h2
            id="how-it-works-heading"
            className="text-2xl sm:text-3xl font-semibold tracking-tight text-content dark:text-content-dark mb-3"
          >
            {t('How TripRadar works')}
          </h2>
          <p className="text-sm sm:text-base text-content-muted dark:text-content-muted-dark max-w-lg mx-auto">
            {t('A simple workflow built for repeat travelers who care about speed and budget control.')}
          </p>
        </header>

        <ol className="space-y-4">
          {steps.map((step, index) => (
            <li
              key={step.title}
              className="flex gap-4 rounded-xl border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-5"
            >
              <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-content dark:bg-content-dark text-surface dark:text-surface-dark text-xs font-semibold">
                {index + 1}
              </span>
              <div className="min-w-0">
                <h3 className="text-sm font-medium text-content dark:text-content-dark">{t(step.title)}</h3>
                <p className="mt-1 text-sm text-content-muted dark:text-content-muted-dark leading-relaxed">
                  {t(step.description)}
                </p>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
};
