import type { LucideIcon } from 'lucide-react';
import {
  BarChart3,
  Globe,
  ListChecks,
  Mail,
  Megaphone,
  RefreshCw,
  Settings,
  ShieldCheck,
  SlidersHorizontal,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { BackToTopButton } from './BackToTopButton';
import { ResponsiveTable } from './ResponsiveTable';
import { SectionHeading } from './SectionHeading';
import { type TocSection, TableOfContents } from './TableOfContents';

interface CookieCategory {
  title: string;
  description: string;
  consentRule: string;
  examples: string[];
  icon: LucideIcon;
}

interface CookieTechnology {
  name: string;
  provider: string;
  category: string;
  purpose: string;
  lifetime: string;
  recipients: string;
}

const cookieUseCases: string[] = [
  'Keep you signed in and protect your account from abuse.',
  'Remember language, region, and interface preferences.',
  'Measure performance to improve reliability and speed.',
  'Support payments, anti-fraud controls, and account recovery journeys.',
];

const cookieCategories: CookieCategory[] = [
  {
    title: 'Strictly Necessary Cookies',
    description:
      'Required for core platform functionality, security, and authentication. If you block these in your browser, the service may not work correctly.',
    consentRule: 'Always active because they are required to provide the service.',
    examples: ['Session authentication tokens', 'Security and fraud prevention controls', 'Load balancing and uptime'],
    icon: ShieldCheck,
  },
  {
    title: 'Functional Cookies',
    description: 'Used to remember your choices and provide a more personalized experience.',
    consentRule: 'In regions that require consent, set only after opt-in.',
    examples: ['Language and regional preferences', 'Theme and accessibility settings', 'Saved UI preferences'],
    icon: SlidersHorizontal,
  },
  {
    title: 'Analytics Cookies',
    description: 'Help us understand traffic and product usage so we can improve TripRadar.',
    consentRule: 'In EU/UK, set only after your explicit opt-in consent.',
    examples: ['Page-level performance metrics', 'Feature adoption measurement', 'Error diagnostics'],
    icon: BarChart3,
  },
  {
    title: 'Advertising Cookies',
    description: 'Used by advertising partners for campaign delivery and measurement when enabled.',
    consentRule: 'In EU/UK, set only after your explicit opt-in consent.',
    examples: ['Campaign attribution', 'Ad frequency capping', 'Audience segmentation'],
    icon: Megaphone,
  },
];

const cookieTechnologies: CookieTechnology[] = [
  {
    name: 'accessToken (cookie)',
    provider: 'TripRadar (first-party)',
    category: 'Strictly Necessary',
    purpose: 'Maintains authenticated API session for signed-in users.',
    lifetime: 'Up to configured access-token lifetime (default: 1000 minutes).',
    recipients: 'TripRadar API only.',
  },
  {
    name: 'refreshToken (cookie)',
    provider: 'TripRadar (first-party)',
    category: 'Strictly Necessary',
    purpose: 'Refreshes authentication session without repeated login.',
    lifetime: '30 days.',
    recipients: 'TripRadar API only.',
  },
  {
    name: 'tripradar-theme (localStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Functional',
    purpose: 'Remembers your light/dark theme preference.',
    lifetime: 'Until changed or deleted by user.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'tripradar.frontendLanguage (localStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Functional',
    purpose: 'Remembers interface language preference.',
    lifetime: 'Until changed or deleted by user.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'tripradar.activeTripVaultUniqueId (localStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Functional',
    purpose: 'Stores the currently selected trip vault context.',
    lifetime: 'Until changed or deleted by user.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'profile_navigation_persistence (sessionStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Functional',
    purpose: 'Preserves unsaved profile form state during active session.',
    lifetime: 'Session.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'registration_email (sessionStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Strictly Necessary',
    purpose: 'Supports account signup and email confirmation flow.',
    lifetime: 'Session.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'telegram_auth_email (sessionStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Strictly Necessary',
    purpose: 'Completes Telegram authentication callback flow.',
    lifetime: 'Session.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'google_oauth_redirect_pending (sessionStorage)',
    provider: 'TripRadar (first-party)',
    category: 'Strictly Necessary',
    purpose: 'Protects OAuth redirect state to prevent duplicate auth flow.',
    lifetime: 'Session.',
    recipients: 'TripRadar only.',
  },
  {
    name: 'Stripe.js cookies/storage (provider-managed keys)',
    provider: 'Stripe, Inc. (third-party)',
    category: 'Strictly Necessary',
    purpose: 'Processes payments and applies anti-fraud controls.',
    lifetime: 'Set by Stripe and may vary by key and jurisdiction.',
    recipients: 'Stripe and TripRadar payment systems.',
  },
  {
    name: 'Firebase Auth storage (provider-managed keys)',
    provider: 'Google LLC (third-party)',
    category: 'Functional / Security',
    purpose: 'Enables Google sign-in and sign-in state continuity.',
    lifetime: 'Set by Firebase SDK and may vary by key and browser.',
    recipients: 'Google (Firebase) and TripRadar auth systems.',
  },
  {
    name: 'Advertising cookies/pixels',
    provider: 'Approved marketing partners (if enabled)',
    category: 'Advertising',
    purpose: 'Cross-site campaign measurement and audience targeting.',
    lifetime: 'Not active by default. Activated only after consent where required.',
    recipients: 'Approved advertising partners only when enabled.',
  },
];

const sectionClassName =
  'bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark rounded-lg p-4 sm:p-5 mb-4';

const tocSections: TocSection[] = [
  { id: 'consent-first', title: 'Consent-first approach' },
  { id: 'what-are-cookies', title: 'What are cookies?' },
  { id: 'why-we-use-cookies', title: 'Why we use cookies' },
  { id: 'types-of-cookies', title: 'Types of cookies we use' },
  { id: 'cookie-inventory', title: 'Cookie and technology inventory' },
  { id: 'manage-cookies', title: 'How to manage cookies' },
  { id: 'third-party-cookies', title: 'Third-party cookies' },
  { id: 'us-privacy-choices', title: 'US privacy choices (including California)' },
  { id: 'consent-records', title: 'Consent records retention' },
  { id: 'policy-updates-contact', title: 'Policy updates and contact' },
];

export const CookiePolicy = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="bg-surface dark:bg-surface-dark transition-colors duration-150 pt-16">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <header className="text-center mb-4">
          <h1 className="text-lg font-semibold text-content dark:text-content-dark mb-2">{t('Cookie Policy')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Last updated: February 24, 2026')}
          </p>
        </header>

        <div className="flex gap-8">
          <TableOfContents sections={tocSections} />

          <div className="min-w-0 flex-1">
            <section id="consent-first" className={sectionClassName}>
              <SectionHeading id="consent-first">{t('Consent-first approach')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'We set Analytics and Advertising cookies only after your consent (opt-in) in EU/UK jurisdictions.'
                  )}
                </p>
                <p>
                  {t(
                    'You can withdraw consent at any time through Cookie Preferences. Withdrawal stops future non-essential storage and access.'
                  )}
                </p>
                <p>
                  {t(
                    'Strictly necessary cookies remain active because they are required to deliver secure login, core service functionality, and fraud prevention.'
                  )}
                </p>
              </div>
            </section>

            <section id="what-are-cookies" className={sectionClassName} style={{ animationDelay: '0.02s' }}>
              <SectionHeading id="what-are-cookies">{t('What are cookies?')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'Cookies are small text files stored in your browser when you visit a website. They help websites recognize your device and store information about your session and preferences.'
                  )}
                </p>
                <p>
                  {t(
                    'We also use related technologies, including local storage, session storage, pixels, and SDK-managed browser storage. The same consent and control rules described in this policy apply to these technologies.'
                  )}
                </p>
              </div>
            </section>

            <section id="why-we-use-cookies" className={sectionClassName} style={{ animationDelay: '0.04s' }}>
              <SectionHeading id="why-we-use-cookies">{t('Why we use cookies')}</SectionHeading>
              <div className="space-y-4">
                {cookieUseCases.map(useCase => (
                  <div key={useCase} className="flex items-start gap-3">
                    <div className="mt-1 h-2.5 w-2.5 rounded-full bg-primary-500 dark:bg-primary-400 flex-shrink-0" />
                    <p className="text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                      {t(useCase)}
                    </p>
                  </div>
                ))}
              </div>
            </section>

            <section id="types-of-cookies" className={sectionClassName} style={{ animationDelay: '0.06s' }}>
              <SectionHeading id="types-of-cookies">{t('Types of cookies we use')}</SectionHeading>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {cookieCategories.map(category => {
                  const Icon = category.icon;

                  return (
                    <article
                      key={category.title}
                      className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4 sm:p-5"
                    >
                      <div className="flex items-start gap-3 mb-3">
                        <div className="p-2 rounded-lg bg-surface dark:bg-surface-dark">
                          <Icon className="h-4 w-4 text-content-muted dark:text-content-muted-dark" />
                        </div>
                        <h3 className="text-sm font-medium text-content dark:text-content-dark">
                          {t(category.title)}
                        </h3>
                      </div>
                      <p className="text-sm text-content-secondary dark:text-content-secondary-dark mb-3">
                        {t(category.description)}
                      </p>
                      <p className="text-xs text-content dark:text-content-dark mb-3 font-medium">
                        {t(category.consentRule)}
                      </p>
                      <ul className="list-disc pl-5 space-y-1 text-sm text-content-secondary dark:text-content-secondary-dark">
                        {category.examples.map(example => (
                          <li key={example}>{t(example)}</li>
                        ))}
                      </ul>
                    </article>
                  );
                })}
              </div>
            </section>

            <section id="cookie-inventory" className={sectionClassName} style={{ animationDelay: '0.08s' }}>
              <SectionHeading id="cookie-inventory" icon={ListChecks}>
                {t('Cookie and technology inventory')}
              </SectionHeading>
              <p className="text-content-secondary dark:text-content-secondary-dark mb-4 leading-relaxed">
                {t(
                  'This inventory lists active and planned storage/access technologies, including first-party and third-party components.'
                )}
              </p>

              <ResponsiveTable
                headers={['Name', 'Provider', 'Category', 'Purpose', 'Lifetime', 'Data recipients']}
                rows={cookieTechnologies.map(row => ({
                  Name: row.name,
                  Provider: row.provider,
                  Category: row.category,
                  Purpose: row.purpose,
                  Lifetime: row.lifetime,
                  'Data recipients': row.recipients,
                }))}
              />
            </section>

            <section id="manage-cookies" className={sectionClassName} style={{ animationDelay: '0.1s' }}>
              <SectionHeading id="manage-cookies" icon={Settings}>
                {t('How to manage cookies')}
              </SectionHeading>

              <p className="text-content-secondary dark:text-content-secondary-dark mb-4 leading-relaxed">
                {t(
                  'Use in-product Cookie Preferences first. Browser controls are additional safeguards but are not a replacement for consent controls.'
                )}
              </p>

              <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4 mb-4">
                <p className="text-content-secondary dark:text-content-secondary-dark mb-3">
                  {t('In-product controls:')}
                </p>
                <ul className="list-disc pl-6 space-y-2 text-content-secondary dark:text-content-secondary-dark">
                  <li>
                    {t('Open')}{' '}
                    <Link
                      to="/cookies#cookie-preferences"
                      className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                    >
                      {t('Cookie Preferences')}
                    </Link>{' '}
                    {t('from the footer or this page.')}
                  </li>
                  <li>{t('Choose Accept all, Reject all, or customize categories, then save your choice.')}</li>
                  <li>{t('You can reset your choice at any time from the same control.')}</li>
                  <li>
                    {t(
                      'If you select Reject all, non-essential technologies are disabled. Core login and security features continue to operate.'
                    )}
                  </li>
                </ul>
              </div>

              <p className="text-content-secondary dark:text-content-secondary-dark mb-2">
                {t('Browser-level controls:')}
              </p>
              <ol className="list-decimal pl-6 space-y-2 text-content-secondary dark:text-content-secondary-dark mb-4">
                <li>{t('Open your browser settings.')}</li>
                <li>{t('Find privacy or cookie controls.')}</li>
                <li>{t('Choose whether to allow, block, or delete cookies.')}</li>
                <li>
                  {t(
                    'If you block strictly necessary cookies in the browser, authentication, payments, and some security flows may fail.'
                  )}
                </li>
              </ol>

              <p className="text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                {t('You can also review our broader data practices in the')}{' '}
                <Link to="/privacy" className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark">
                  {t('Privacy Policy')}
                </Link>{' '}
                {t('and service terms in the')}{' '}
                <Link to="/terms" className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark">
                  {t('Terms of Service')}
                </Link>
                .
              </p>
            </section>

            <section
              id="third-party-cookies"
              className={sectionClassName}
            >
              <SectionHeading
                id="third-party-cookies"
                icon={Globe}
              >
                {t('Third-party cookies')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'Third parties may provide analytics, anti-fraud, payment processing, infrastructure/CDN, and authentication components. Their technologies are governed by both this policy and their own privacy notices.'
                  )}
                </p>
                <p>
                  {t('Our current provider list is maintained in')}{' '}
                  <Link to="/privacy#subprocessors" className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark">
                    {t('Third-party providers and subprocessors')}
                  </Link>
                  {t('. We update that list when provider scope changes.')}
                </p>
              </div>
            </section>

            <section id="us-privacy-choices" className={sectionClassName} style={{ animationDelay: '0.14s' }}>
              <SectionHeading id="us-privacy-choices">{t('US privacy choices (including California)')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'Where U.S. state privacy laws apply, we provide an opt-out mechanism for uses that qualify as sale/share of personal information for cross-context behavioral advertising.'
                  )}
                </p>
                <p>
                  {t('Submit an opt-out request through')}{' '}
                  <a
                    href="mailto:privacy@tripradar.io?subject=Do%20Not%20Sell%20or%20Share%20My%20Personal%20Information"
                    className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                  >
                    {t('Do Not Sell or Share My Personal Information')}
                  </a>
                  .
                </p>
                <p>
                  {t(
                    'We process recognized opt-out preference signals, including Global Privacy Control (GPC), as opt-out requests where legally required.'
                  )}
                </p>
              </div>
            </section>

            <section id="consent-records" className={sectionClassName} style={{ animationDelay: '0.16s' }}>
              <SectionHeading id="consent-records">{t('Consent records retention')}</SectionHeading>
              <p className="text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                {t(
                  'We keep consent records (choice state, timestamp, policy version, locale, and source signal) for 5 years unless a longer period is required by law or active dispute handling.'
                )}
              </p>
            </section>

            <section id="policy-updates-contact" className={sectionClassName} style={{ animationDelay: '0.18s' }}>
              <SectionHeading id="policy-updates-contact" icon={RefreshCw}>
                {t('Policy updates and contact')}
              </SectionHeading>

              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    `We may update this Cookie Policy from time to time. Material changes will be reflected by updating the "Last updated" date at the top of this page.`
                  )}
                </p>

                <div className="flex items-start gap-3">
                  <Mail className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <p>
                    {t('Questions about cookies or privacy can be sent to')}{' '}
                    <a
                      href="mailto:privacy@tripradar.io"
                      className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                    >
                      privacy@tripradar.io
                    </a>
                    .
                  </p>
                </div>
              </div>
            </section>
          </div>
        </div>
      </div>

      <BackToTopButton />
    </div>
  );
};
