import type { LucideIcon } from 'lucide-react';
import { CreditCard, MessageSquareText, ShieldCheck, Sparkles } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { ROUTES } from 'shared/config/routes';
import { BackToTopButton } from './BackToTopButton';

interface HelpTopic {
  title: string;
  description: string;
  actionLabel: string;
  actionHref: string;
  icon: LucideIcon;
}

interface FaqItem {
  question: string;
  answer: string;
}

const helpTopics: HelpTopic[] = [
  {
    title: 'Getting started',
    description: 'Set up your account, personalize your preferences, and start your first trip plan in minutes.',
    actionLabel: 'Create your account',
    actionHref: ROUTES.SIGNUP,
    icon: Sparkles,
  },
  {
    title: 'Plans and billing',
    description: 'Compare plans, switch tiers, and understand what is included in each subscription.',
    actionLabel: 'View pricing',
    actionHref: ROUTES.PRICING,
    icon: CreditCard,
  },
  {
    title: 'Account and security',
    description: 'Review privacy controls, security settings, and account protection best practices.',
    actionLabel: 'Read privacy policy',
    actionHref: ROUTES.PRIVACY,
    icon: ShieldCheck,
  },
  {
    title: 'Feedback and support',
    description: 'Send product feedback or ask support for help with your account and trip workflows.',
    actionLabel: 'Open feedback page',
    actionHref: ROUTES.FEEDBACK,
    icon: MessageSquareText,
  },
];

const faqItems: FaqItem[] = [
  {
    question: 'How do I start planning my first trip?',
    answer:
      'Create an account, sign in, and open your profile. From there, you can configure your preferences and begin tracking requests and saved trip details.',
  },
  {
    question: 'Where can I manage my subscription?',
    answer: 'You can compare plans on the Pricing page and manage billing details from your profile after signing in.',
  },
  {
    question: 'How do I reset my password?',
    answer:
      'Use the Forgot Password flow from the login screen. We will send password reset instructions to your account email.',
  },
  {
    question: 'How quickly can I expect a support response?',
    answer:
      'For most requests, we respond within one business day. Include as much context as possible to speed up troubleshooting.',
  },
];

export const HelpCenter = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="bg-surface dark:bg-surface-dark transition-colors duration-150 pt-16">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8 flex flex-col gap-4">
        <header className="text-center mb-4">
          <h1 className="text-lg font-semibold text-content dark:text-content-dark mb-2">{t('Help Center')}</h1>
        </header>

        <section className="flex flex-col gap-3">
          <h2 className="text-base font-medium text-content dark:text-content-dark">{t('Popular topics')}</h2>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            {helpTopics.map(topic => {
              const Icon = topic.icon;

              return (
                <article
                  key={topic.title}
                  className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5 flex flex-col gap-4"
                >
                  <div className="flex items-start gap-3">
                    <div className="rounded-lg bg-surface-accent dark:bg-surface-accent-dark p-2">
                      <Icon className="h-4 w-4 text-content-muted dark:text-content-muted-dark" />
                    </div>
                    <div className="flex flex-col gap-1">
                      <h3 className="text-sm font-medium text-content dark:text-content-dark">{t(topic.title)}</h3>
                      <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
                        {t(topic.description)}
                      </p>
                    </div>
                  </div>

                  <Link
                    to={topic.actionHref}
                    className="w-fit inline-flex items-center rounded-lg bg-surface-accent dark:bg-surface-accent-dark px-3 py-1.5 text-xs font-medium text-content dark:text-content-dark hover:bg-surface-accent-dark-hover dark:hover:bg-surface-accent-dark-hover transition-colors duration-150"
                  >
                    {t(topic.actionLabel)}
                  </Link>
                </article>
              );
            })}
          </div>
        </section>

        <section className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5 flex flex-col gap-3">
          <h2 className="text-base font-medium text-content dark:text-content-dark">
            {t('Frequently asked questions')}
          </h2>
          <div className="flex flex-col gap-2">
            {faqItems.map(item => (
              <details
                key={item.question}
                className="group rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark px-4 py-3"
              >
                <summary className="cursor-pointer list-none text-sm font-medium text-content dark:text-content-dark flex items-center justify-between gap-4">
                  {t(item.question)}
                  <span className="text-content-muted dark:text-content-muted-dark group-open:rotate-45 transition-transform text-sm leading-none">
                    +
                  </span>
                </summary>
                <p className="pt-3 text-sm text-content-secondary dark:text-content-secondary-dark">
                  {t(item.answer)}
                </p>
              </details>
            ))}
          </div>
        </section>

        <section className="rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5 flex flex-col gap-3">
          <h2 className="text-base font-medium text-content dark:text-content-dark">{t('Still need help?')}</h2>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t(
              'Send us a message and include your account email plus a short description of the issue. We will guide you through the next steps.'
            )}
          </p>
          <div className="flex flex-wrap gap-2">
            <a
              href="mailto:support@tripradar.io"
              className="inline-flex items-center rounded-lg bg-button dark:bg-button-dark px-4 py-2.5 text-sm font-medium text-button-text dark:text-button-text-dark hover:bg-button-hover dark:hover:bg-button-hover-dark transition-colors duration-150"
            >
              support@tripradar.io
            </a>
            <Link
              to={ROUTES.FEEDBACK}
              className="inline-flex items-center rounded-lg border border-outline dark:border-outline-dark px-4 py-2.5 text-sm font-medium text-content dark:text-content-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors duration-150"
            >
              {t('Share feedback')}
            </Link>
          </div>
        </section>
      </div>
      <BackToTopButton />
    </div>
  );
};
