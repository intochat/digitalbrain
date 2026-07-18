import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';

interface GuidePageProps {
  title: string;
  description: string;
  highlights: string[];
  actionLabel: string;
  actionHref: string;
}

const GuidePage = ({ title, description, highlights, actionLabel, actionHref }: GuidePageProps) => {
  const { t } = useFrontendLanguage();
  const isExternalAction = actionHref.startsWith('http://') || actionHref.startsWith('https://');

  return (
    <div className="min-h-screen bg-surface dark:bg-surface-dark pt-20 pb-16 px-4 sm:px-6 lg:px-8">
      <article className="max-w-4xl mx-auto rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-6 sm:p-8 lg:p-10">
        <header className="mb-6">
          <p className="text-xs sm:text-sm uppercase tracking-wide text-content-secondary dark:text-content-secondary-dark mb-3">
            {t('TripRadar Guide')}
          </p>
          <h1 className="text-3xl sm:text-4xl font-semibold text-content dark:text-content-dark leading-tight mb-4">
            {t(title)}
          </h1>
          <p className="text-base sm:text-lg text-content-secondary dark:text-content-secondary-dark leading-relaxed">
            {t(description)}
          </p>
        </header>

        <section className="mb-8">
          <h2 className="text-xl sm:text-2xl font-semibold text-content dark:text-content-dark mb-4">
            {t('What to focus on')}
          </h2>
          <ul className="space-y-3">
            {highlights.map(item => (
              <li
                key={item}
                className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark px-4 py-3 text-sm sm:text-base text-content dark:text-content-dark"
              >
                {t(item)}
              </li>
            ))}
          </ul>
        </section>

        <footer className="flex flex-wrap gap-3">
          {isExternalAction ? (
            <a
              href={actionHref}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center rounded-xl bg-button dark:bg-button-dark px-5 py-3 text-sm font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors"
            >
              {t(actionLabel)}
            </a>
          ) : (
            <Link
              to={actionHref}
              className="inline-flex items-center rounded-xl bg-button dark:bg-button-dark px-5 py-3 text-sm font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors"
            >
              {t(actionLabel)}
            </Link>
          )}
          <Link
            to={ROUTES.PRICING}
            className="inline-flex items-center rounded-xl border border-outline dark:border-outline-dark px-5 py-3 text-sm font-medium text-content dark:text-content-dark hover:bg-surface dark:hover:bg-surface-dark transition-colors"
          >
            {t('See Pricing')}
          </Link>
        </footer>
      </article>
    </div>
  );
};

export const TelegramTripPlannerGuide = () => (
  <GuidePage
    title="Telegram trip planner for frequent travelers"
    description="Use Telegram as your planning control panel so every itinerary, budget assumption, and decision stays in one place."
    highlights={[
      'Start each trip with destination, dates, and budget constraints in a single prompt.',
      'Review route and spend options before booking to reduce expensive plan changes.',
      'Reuse saved trip context when planning your next weekend or work trip.',
    ]}
    actionLabel="Start planning in Telegram"
    actionHref="https://t.me/TripRadarBot"
  />
);

export const BudgetAiPlannerGuide = () => (
  <GuidePage
    title="AI trip planning for budget travelers"
    description="Budget-first planning works best when routing and spend checks happen before booking pressure starts."
    highlights={[
      'Define acceptable spend ranges before comparing options.',
      'Prioritize plans that reduce hidden costs and coordination overhead.',
      'Track past trip outcomes to tune future budget assumptions.',
    ]}
    actionLabel="Create your free account"
    actionHref={ROUTES.SIGNUP}
  />
);

export const AlternativesGuide = () => (
  <GuidePage
    title="Trip planning assistant alternatives"
    description="Compare manual planning stacks and assistant workflows by speed, budget clarity, and repeatability."
    highlights={[
      'Manual tabs + spreadsheets are flexible but often slow under time pressure.',
      'Single-chat planning improves consistency when you travel frequently.',
      'Choose a workflow that captures history so each trip gets easier.',
    ]}
    actionLabel="Compare plans"
    actionHref={ROUTES.PRICING}
  />
);

export const BudgetGuide2026 = () => (
  <GuidePage
    title="How to plan a trip budget in 2026"
    description="Use a simple constraint-first model: destination, date window, hard budget limits, then route validation."
    highlights={[
      'Set hard limits first, then evaluate route and stay tradeoffs.',
      'Keep one source of truth for assumptions, edits, and final picks.',
      'Audit post-trip spending to improve your next budget baseline.',
    ]}
    actionLabel="Get trip planning help"
    actionHref={ROUTES.HELP}
  />
);

export const ChecklistTemplateGuide = () => (
  <GuidePage
    title="Trip checklist and budget template"
    description="A repeatable checklist removes planning guesswork and lowers decision fatigue for recurring trips."
    highlights={[
      'Lock destination, timing, budget, and non-negotiables before searching deeply.',
      'Keep a shortlist of route and stay options with tradeoff notes.',
      'Save final choices and outcomes to reuse your best template next time.',
    ]}
    actionLabel="Open Help Center"
    actionHref={ROUTES.HELP}
  />
);

export const ManualVsTripRadarGuide = () => (
  <GuidePage
    title="Manual planning vs TripRadar"
    description="Manual workflows are familiar, but repeat travelers often benefit from a centralized Telegram-first process."
    highlights={[
      'Manual process: maximum flexibility, higher coordination cost.',
      'TripRadar process: faster setup, clearer budget control, reusable history.',
      'Best choice depends on how often you travel and how much structure you need.',
    ]}
    actionLabel="Try TripRadar"
    actionHref={ROUTES.SIGNUP}
  />
);

export const ExampleTripPlanGuide = () => (
  <GuidePage
    title="Example trip plan: 4 days in Lisbon under budget"
    description="A sample TripRadar output showing how destination, budget limits, and must-do preferences become a practical plan in Telegram."
    highlights={[
      'Input: Lisbon, 4 days, budget cap 650 EUR, 2 museum visits, central location, avoid red-eye flights.',
      'Output: route shortlist, stay options by total cost, and day-by-day checklist in one thread.',
      'Decision logic: fallback options are included when first-choice transport or stays exceed budget.',
      'Final vault: assumptions, chosen route, expected spend, and next-trip reuse notes.',
    ]}
    actionLabel="Start this flow in Telegram"
    actionHref="https://t.me/TripRadarBot"
  />
);

export const SavingsMethodologyGuide = () => (
  <GuidePage
    title="How TripRadar calculates planning savings"
    description="Savings claims are based on anonymized cohorts comparing manual planning against TripRadar-assisted decisions under similar route and date constraints."
    highlights={[
      'Time savings: median planning time delta between manual workflow and Telegram-first workflow.',
      'Budget savings: reduced avoidable re-bookings and better first-pass option selection for matched trips.',
      'Data scope: 2026 anonymized records with outlier filtering and route-level normalization.',
      'Limitations: outcomes vary by destination seasonality, traveler preferences, and booking lead time.',
    ]}
    actionLabel="Review pricing plans"
    actionHref={ROUTES.PRICING}
  />
);
