import {
  AlertTriangle,
  CheckCircle,
  Clock,
  Database,
  ExternalLink,
  FileText,
  Globe,
  Lock,
  Mail,
  MapPin,
  UserCheck,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { BackToTopButton } from './BackToTopButton';
import { ResponsiveTable } from './ResponsiveTable';
import { SectionHeading } from './SectionHeading';
import { type TocSection, TableOfContents } from './TableOfContents';

interface MatrixRow {
  purpose: string;
  dataCategories: string;
  lawfulBasis: string;
}

interface RetentionRow {
  category: string;
  retention: string;
}

interface CcpaRow {
  category: string;
  examples: string;
  recipients: string;
}

const sectionClassName =
  'bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark rounded-lg p-4 sm:p-5 mb-4';

const tocSections: TocSection[] = [
  { id: 'who-we-are', title: 'Who We Are and Data Roles' },
  { id: 'data-we-collect', title: 'Data We Collect and Sources' },
  { id: 'purpose-and-lawful-basis', title: 'Purpose, Data Categories, and Lawful Basis' },
  { id: 'data-retention', title: 'Data Retention' },
  { id: 'international-transfers', title: 'International Data Transfers' },
  { id: 'ai-profiling', title: 'AI, Profiling, and Automated Processing' },
  { id: 'disclosure-recipients', title: 'Disclosure and Recipients' },
  { id: 'required-data', title: 'Required Data and Consequences of Non-Disclosure' },
  { id: 'cookies', title: 'Cookies and Tracking Technologies' },
  { id: 'your-rights', title: 'Your Rights and Request Process' },
  { id: 'children', title: 'Children and Minors' },
  { id: 'security', title: 'Security and Incident Response' },
  { id: 'us-privacy-rights', title: 'U.S. Privacy Rights (CCPA/CPRA)' },
  { id: 'policy-updates', title: 'Policy Updates' },
  { id: 'contact-us', title: 'Contact Us' },
];

const lawfulBasisRows: MatrixRow[] = [
  {
    purpose: 'Account creation, authentication, and profile management',
    dataCategories: 'Email, password hash, account identifiers, sign-in metadata',
    lawfulBasis: 'Contract performance; legitimate interests in service security',
  },
  {
    purpose: 'Trip planning, itinerary generation, and travel recommendations',
    dataCategories: 'Trip inputs, destinations, preferences, travel history, support context',
    lawfulBasis: 'Contract performance; legitimate interests in personalization',
  },
  {
    purpose: 'Payment processing and subscription management',
    dataCategories: 'Billing profile, payment tokens, plan, invoices, tax records',
    lawfulBasis: 'Contract performance; legal obligation (tax/accounting)',
  },
  {
    purpose: 'Fraud prevention, abuse detection, and platform security',
    dataCategories: 'IP address, device/browser data, auth events, risk indicators',
    lawfulBasis: 'Legitimate interests; legal obligation where applicable',
  },
  {
    purpose: 'Marketing communications and analytics cookies/technologies',
    dataCategories: 'Marketing preferences, usage analytics, campaign attribution data',
    lawfulBasis: 'Consent (where required); legitimate interests (where permitted)',
  },
  {
    purpose: 'Legal compliance, dispute handling, and rights requests',
    dataCategories: 'Correspondence, account records, regulatory disclosures, audit logs',
    lawfulBasis: 'Legal obligation; legitimate interests in claims defense',
  },
];

const retentionRows: RetentionRow[] = [
  {
    category: 'Account profile and core service records',
    retention: 'For account lifetime and up to 24 months after closure, unless law requires longer retention.',
  },
  {
    category: 'Trip history and travel context',
    retention: 'Until deleted by user or up to 24 months after account closure.',
  },
  {
    category: 'Payment and invoice records',
    retention: 'Typically 7 years for tax/accounting obligations, subject to local law.',
  },
  {
    category: 'Support tickets and complaint files',
    retention: 'Up to 36 months after resolution, unless active dispute requires longer storage.',
  },
  {
    category: 'Security logs and anti-fraud telemetry',
    retention: 'Typically 12 months, or longer when needed for incident investigation.',
  },
  {
    category: 'Consent and preference records',
    retention: '5 years or longer if required by law or dispute handling.',
  },
];

const ccpaRows: CcpaRow[] = [
  {
    category: 'Identifiers',
    examples: 'Email, account ID, IP address, device identifiers',
    recipients: 'Authentication providers, hosting/security vendors, support tools',
  },
  {
    category: 'Commercial information',
    examples: 'Subscription plan, invoices, payment status',
    recipients: 'Payment processors, accounting and finance systems',
  },
  {
    category: 'Internet/network activity',
    examples: 'Usage events, page interactions, diagnostics',
    recipients: 'Infrastructure, telemetry, and analytics providers',
  },
  {
    category: 'Geolocation and travel context',
    examples: 'Trip destinations, route preferences, itinerary metadata',
    recipients: 'Travel-data integrations and recommendation systems',
  },
];

export const PrivacyPolicy = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="bg-surface dark:bg-surface-dark transition-colors duration-150 pt-16">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <div className="text-center mb-4">
          <h1 className="text-lg font-semibold text-content dark:text-content-dark mb-2">{t('Privacy Policy')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Last updated: February 24, 2026')}
          </p>
        </div>

        <div className="flex gap-8">
          <TableOfContents sections={tocSections} />

          <div className="min-w-0 flex-1">
            <section id="who-we-are" className={sectionClassName}>
              <SectionHeading id="who-we-are">{t('Who We Are and Data Roles')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'TripRadar Inc. is the primary controller of Personal Data processed through consumer TripRadar accounts and services. Operational mailing location: Kyiv, Ukraine.'
                  )}
                </p>
                <p>
                  {t(
                    'For managed enterprise travel programs, your employer or corporate customer may act as the controller for specific processing activities, with TripRadar acting as processor under contractual instructions.'
                  )}
                </p>
                <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4">
                  <ul className="list-disc pl-6 space-y-2">
                    <li>{t('Privacy contact and privacy lead: privacy@tripradar.io')}</li>
                    <li>{t('General support and legal contact: support@tripradar.io')}</li>
                    <li>
                      {t(
                        'If a Data Protection Officer (DPO) or EU/UK representative is appointed for covered processing, their contact details are published in this section without delay.'
                      )}
                    </li>
                  </ul>
                </div>
              </div>
            </section>

            <section id="data-we-collect" className={sectionClassName} style={{ animationDelay: '0.015s' }}>
              <SectionHeading id="data-we-collect" icon={Database}>
                {t('Data We Collect and Sources')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>{t('We collect Personal Data from the following sources:')}</p>
                <ul className="list-disc pl-6 space-y-2">
                  <li>{t('Directly from you (account creation, profile fields, trip inputs, support requests).')}</li>
                  <li>
                    {t(
                      'Automatically from your device and browser (logs, diagnostics, security telemetry, usage events).'
                    )}
                  </li>
                  <li>
                    {t('From third parties (payment processors, authentication providers, travel-data integrations).')}
                  </li>
                  <li>
                    {t(
                      'From corporate customers/employers where your account is provisioned under a business travel arrangement.'
                    )}
                  </li>
                </ul>
                <p>
                  {t(
                    'Payment card numbers are processed by our payment processor. TripRadar stores payment-related identifiers and tokens, not raw full card data.'
                  )}
                </p>
              </div>
            </section>

            <section id="purpose-and-lawful-basis" className={sectionClassName} style={{ animationDelay: '0.03s' }}>
              <SectionHeading id="purpose-and-lawful-basis">
                {t('Purpose, Data Categories, and Lawful Basis')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed">
                <p className="text-content-secondary dark:text-content-secondary-dark">
                  {t(
                    'The table below maps major processing purposes to data categories and lawful bases under GDPR/UK GDPR logic.'
                  )}
                </p>
                <ResponsiveTable
                  headers={['Purpose', 'Data categories', 'Lawful basis']}
                  rows={lawfulBasisRows.map(r => ({
                    Purpose: r.purpose,
                    'Data categories': r.dataCategories,
                    'Lawful basis': r.lawfulBasis,
                  }))}
                />
              </div>
            </section>

            <section id="data-retention" className={sectionClassName} style={{ animationDelay: '0.045s' }}>
              <SectionHeading id="data-retention">{t('Data Retention')}</SectionHeading>
              <div className="space-y-4 leading-relaxed">
                <p className="text-content-secondary dark:text-content-secondary-dark">
                  {t(
                    'We retain Personal Data only as long as necessary for the purposes described in this policy or as required by law.'
                  )}
                </p>
                <ResponsiveTable
                  headers={['Data category', 'Retention period or criteria']}
                  rows={retentionRows.map(r => ({
                    'Data category': r.category,
                    'Retention period or criteria': r.retention,
                  }))}
                />
              </div>
            </section>

            <section id="international-transfers" className={sectionClassName} style={{ animationDelay: '0.06s' }}>
              <SectionHeading id="international-transfers" icon={Globe}>
                {t('International Data Transfers')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'Personal Data may be processed in countries where TripRadar or its providers operate, including Ukraine, EU/EEA jurisdictions, the United States, and other provider locations.'
                  )}
                </p>
                <p>
                  {t(
                    'Where transfers from the EEA/UK occur to countries without an adequacy decision, we rely on appropriate safeguards such as Standard Contractual Clauses (SCCs) and the UK International Data Transfer Addendum, plus technical and contractual controls.'
                  )}
                </p>
              </div>
            </section>

            <section
              id="ai-profiling"
              className={sectionClassName}
            >
              <SectionHeading
                id="ai-profiling"
                icon={Lock}
              >
                {t('AI, Profiling, and Automated Processing')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'TripRadar uses automated processing to rank travel options, generate recommendations, and detect abuse or fraud patterns. This may involve limited profiling based on your preferences, historical interactions, and session risk signals.'
                  )}
                </p>
                <p>
                  {t(
                    'Consequences may include personalized itinerary ordering, tailored suggestions, or temporary security checks. If a decision has legal or similarly significant effects, you can request human review where required by law.'
                  )}
                </p>
                <p>
                  {t(
                    'Security controls include encryption in transit (TLS), encryption at rest, access controls, and least-privilege operations. We avoid claiming universal end-to-end encryption for all service components.'
                  )}
                </p>
                <p>
                  {t(
                    'Local processing is used for selected client-side functions (for example UI preferences and temporary browser state). Core recommendation and account features may require server-side processing.'
                  )}
                </p>
              </div>
            </section>

            <section id="disclosure-recipients" className={sectionClassName} style={{ animationDelay: '0.09s' }}>
              <SectionHeading id="disclosure-recipients" icon={FileText}>
                {t('Disclosure and Recipients')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>{t('We share Personal Data with recipients that need it for defined service purposes:')}</p>
                <ul className="list-disc pl-6 space-y-2">
                  <li>{t('Payment processors and anti-fraud vendors (for subscriptions and payment security).')}</li>
                  <li>{t('Authentication providers (for social sign-in and account security).')}</li>
                  <li>
                    {t('Cloud hosting, CDN, observability, and support vendors (service delivery and reliability).')}
                  </li>
                  <li>
                    {t('Travel-related providers and integrations needed to fulfill requested travel functionality.')}
                  </li>
                  <li>{t('Authorities or counterparties when required by law, litigation, or rights protection.')}</li>
                </ul>
                <p>
                  {t(
                    'This subprocessor list and category mapping is reviewed and updated when provider scope changes. Third parties may apply their own terms where they act as independent controllers.'
                  )}
                </p>
              </div>
            </section>

            <section id="required-data" className={sectionClassName} style={{ animationDelay: '0.105s' }}>
              <SectionHeading id="required-data">
                {t('Required Data and Consequences of Non-Disclosure')}
              </SectionHeading>
              <ul className="list-disc pl-6 space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <li>{t('Account credentials are required to authenticate and secure your account.')}</li>
                <li>{t('Billing data is required to start or maintain paid subscriptions.')}</li>
                <li>{t('Certain travel details are required to generate or complete travel-related requests.')}</li>
                <li>{t('If required data is not provided, related features may be unavailable or degraded.')}</li>
              </ul>
            </section>

            <section id="cookies" className={sectionClassName} style={{ animationDelay: '0.12s' }}>
              <SectionHeading id="cookies">{t('Cookies and Tracking Technologies')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>{t('We use strictly necessary, functional, analytics, and advertising technologies.')}</p>
                <p>
                  {t(
                    'Where required by law, non-essential technologies are set only after consent. You can update preferences and withdraw consent at any time.'
                  )}
                </p>
                <p>
                  {t(
                    'Consent records are retained for auditability. For full details, categories, and inventory, see our full Cookie Policy.'
                  )}
                </p>
              </div>
              <div className="mt-4">
                <Link
                  to="/cookies#cookie-preferences"
                  className="text-content dark:text-content-dark font-medium underline hover:text-content-secondary dark:hover:text-content-secondary-dark flex items-center space-x-1"
                >
                  <span>{t('View our full Cookies Policy')}</span>
                  <ExternalLink className="h-4 w-4" />
                </Link>
              </div>
            </section>

            <section id="your-rights" className={sectionClassName} style={{ animationDelay: '0.135s' }}>
              <SectionHeading id="your-rights" icon={UserCheck}>
                {t('Your Rights and Request Process')}
              </SectionHeading>
              <ul className="space-y-4">
                {[
                  'Right of access',
                  'Right to rectification',
                  'Right to erasure',
                  'Right to restrict processing',
                  'Right to data portability',
                  'Right to object to processing based on legitimate interests',
                  'Right to withdraw consent at any time (where processing is based on consent)',
                ].map(right => (
                  <li key={right} className="flex items-start space-x-3">
                    <CheckCircle className="h-5 w-5 text-green-500 mt-0.5 flex-shrink-0" />
                    <span className="text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                      {t(right)}
                    </span>
                  </li>
                ))}
              </ul>

              <div className="mt-4 rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4">
                <p className="text-content-secondary dark:text-content-secondary-dark mb-2">
                  {t('Request handling process:')}
                </p>
                <ul className="list-disc pl-6 space-y-2 text-content-secondary dark:text-content-secondary-dark leading-relaxed">
                  <li>{t('We acknowledge most requests within 48 hours.')}</li>
                  <li>
                    {t(
                      'Substantive response target is one month, extendable where legally permitted for complex requests.'
                    )}
                  </li>
                  <li>{t('We may verify identity before disclosing or deleting data.')}</li>
                  <li>{t('Where applicable, authorized agents may submit requests under local law requirements.')}</li>
                  <li>
                    {t(
                      'You may lodge a complaint with your local supervisory authority (including EEA/UK authorities where relevant).'
                    )}
                  </li>
                </ul>
              </div>
            </section>

            <section id="children" className={sectionClassName} style={{ animationDelay: '0.15s' }}>
              <SectionHeading id="children">{t('Children and Minors')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'The Service is not directed to children under 16. We do not knowingly collect Personal Data from children under 16 without legally valid authorization.'
                  )}
                </p>
                <p>
                  {t(
                    'If you believe a child has provided Personal Data unlawfully, contact privacy@tripradar.io for review and deletion steps.'
                  )}
                </p>
              </div>
            </section>

            <section id="security" className={sectionClassName} style={{ animationDelay: '0.165s' }}>
              <SectionHeading id="security" icon={Lock}>
                {t('Security and Incident Response')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'We use technical and organizational safeguards such as encryption, access controls, network protection, monitoring, and secure development practices. No system is absolutely secure.'
                  )}
                </p>
                <p>
                  {t(
                    'If a security incident affects Personal Data, we follow incident-response procedures and notify affected users and regulators when required by applicable law.'
                  )}
                </p>
              </div>
            </section>

            <section id="us-privacy-rights" className={sectionClassName} style={{ animationDelay: '0.18s' }}>
              <SectionHeading id="us-privacy-rights" icon={Globe}>
                {t('U.S. Privacy Rights (CCPA/CPRA)')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed">
                <p className="text-content-secondary dark:text-content-secondary-dark">
                  {t(
                    'For California and other applicable U.S. state residents, this section summarizes categories of Personal Information, purposes, and recipient categories.'
                  )}
                </p>
                <ResponsiveTable
                  headers={['Category of PI', 'Examples', 'Recipients']}
                  rows={ccpaRows.map(r => ({
                    'Category of PI': r.category,
                    Examples: r.examples,
                    Recipients: r.recipients,
                  }))}
                />
                <div className="space-y-4 text-content-secondary dark:text-content-secondary-dark">
                  <p>{t('TripRadar does not sell Personal Information for money.')}</p>
                  <p>
                    {t(
                      'Where sharing for cross-context behavioral advertising is legally relevant, you can opt out by using our Do Not Sell or Share mechanism.'
                    )}{' '}
                    <a
                      href="mailto:privacy@tripradar.io?subject=Do%20Not%20Sell%20or%20Share%20My%20Personal%20Information"
                      className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                    >
                      {t('Do Not Sell or Share My Personal Information')}
                    </a>
                    .
                  </p>
                  <p>
                    {t('We honor recognized browser opt-out preference signals, including GPC, where required by law.')}
                  </p>
                </div>
              </div>
            </section>

            <section id="policy-updates" className={sectionClassName} style={{ animationDelay: '0.195s' }}>
              <SectionHeading id="policy-updates" icon={Clock}>
                {t('Policy Updates')}
              </SectionHeading>
              <p className="leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                {t(
                  'We may update this Privacy Policy when legal, product, or provider changes require it. Material updates are announced through in-product notice, email, or legal pages, and reflected by updating the Last updated date.'
                )}
              </p>
            </section>

            <section id="contact-us" className={sectionClassName} style={{ animationDelay: '0.2s' }}>
              <SectionHeading id="contact-us" icon={Mail}>
                {t('Contact Us')}
              </SectionHeading>

              <p className="leading-relaxed text-content-secondary dark:text-content-secondary-dark mb-6">
                {t('If you have questions or privacy requests, contact us:')}
              </p>

              <div className="space-y-4">
                <div className="flex items-start space-x-3">
                  <Mail className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">{t('Privacy contact:')}</span>
                    <p className="text-content-secondary dark:text-content-secondary-dark mt-1">
                      <a
                        href="mailto:privacy@tripradar.io"
                        className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                      >
                        privacy@tripradar.io
                      </a>
                    </p>
                  </div>
                </div>

                <div className="flex items-start space-x-3">
                  <Mail className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">{t('General support:')}</span>
                    <p className="text-content-secondary dark:text-content-secondary-dark mt-1">
                      <a
                        href="mailto:support@tripradar.io"
                        className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark"
                      >
                        support@tripradar.io
                      </a>
                    </p>
                  </div>
                </div>

                <div className="flex items-start space-x-3">
                  <MapPin className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">{t('Mailing location:')}</span>
                    <p className="text-content-secondary dark:text-content-secondary-dark mt-1">
                      {t('TripRadar Inc.')}
                      <br />
                      {t('Kyiv')}
                      <br />
                      {t('Ukraine')}
                    </p>
                  </div>
                </div>

                <div className="flex items-start space-x-3">
                  <Clock className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">{t('Response timeline:')}</span>
                    <p className="text-content-secondary dark:text-content-secondary-dark mt-1">
                      {t(
                        'We target acknowledgement within 48 hours and formal response within one month, subject to legal extensions where applicable.'
                      )}
                    </p>
                  </div>
                </div>

                <div className="flex items-start space-x-3">
                  <AlertTriangle className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark mt-0.5" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">
                      {t('Regulatory complaints:')}
                    </span>
                    <p className="text-content-secondary dark:text-content-secondary-dark mt-1">
                      {t(
                        'You may lodge a complaint with the data protection authority in your country, including the EEA/UK authority relevant to your residence.'
                      )}
                    </p>
                  </div>
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
