import {
  AlertTriangle,
  Ban,
  Clock,
  CreditCard,
  FileText,
  Globe,
  Gavel,
  HelpCircle,
  Mail,
  Plane,
  Shield,
  UserX,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { BackToTopButton } from './BackToTopButton';
import { SectionHeading } from './SectionHeading';
import { type TocSection, TableOfContents } from './TableOfContents';

const sectionClassName =
  'bg-surface dark:bg-surface-dark border border-outline dark:border-outline-dark rounded-lg p-4 sm:p-5 mb-4';

const tocSections: TocSection[] = [
  { id: 'introduction-and-definitions', title: 'Introduction and Definitions' },
  { id: 'travel-services-disclaimer', title: 'Travel Services Disclaimer' },
  { id: 'accounts-and-security', title: 'Accounts and Security' },
  { id: 'acceptable-use', title: 'Acceptable Use and Restrictions' },
  { id: 'subscriptions', title: 'Subscriptions, Auto-Renewal, Cancellation, and Refunds' },
  { id: 'intellectual-property', title: 'Intellectual Property and User Content' },
  { id: 'suspension-termination', title: 'Suspension, Termination, and Data Handling' },
  { id: 'disclaimers-of-warranties', title: 'Disclaimers of Warranties' },
  { id: 'limitation-of-liability', title: 'Limitation of Liability' },
  { id: 'copyright-complaints', title: 'Copyright and Abuse Complaints' },
  { id: 'governing-law', title: 'Governing Law and Disputes' },
  { id: 'sanctions-compliance', title: 'Sanctions and Export Compliance' },
  { id: 'notices-and-changes', title: 'Notices and Changes' },
  { id: 'contact-us', title: 'Contact Us' },
];

export const TermsOfService = () => {
  const { t } = useFrontendLanguage();

  return (
    <div className="bg-surface dark:bg-surface-dark transition-colors duration-150 pt-16">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <header className="text-center mb-4">
          <h1 className="text-lg font-semibold text-content dark:text-content-dark mb-2">{t('Terms of Service')}</h1>
          <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
            {t('Last updated: February 24, 2026')}
          </p>
        </header>

        <div className="flex gap-8">
          <TableOfContents sections={tocSections} />
          <div className="min-w-0 flex-1">
            <section id="introduction-and-definitions" className={sectionClassName}>
              <SectionHeading id="introduction-and-definitions">{t('Introduction and Definitions')}</SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'These Terms of Service govern your use of the TripRadar website, apps, APIs, and related services (the "Service"). By using the Service, you agree to these Terms.'
                  )}
                </p>
                <p>
                  {t(
                    'The Service is operated by TripRadar Inc. ("TripRadar", "we", "us"). Primary support contact: support@tripradar.io. Privacy contact: privacy@tripradar.io. Operational mailing location: Kyiv, Ukraine.'
                  )}
                </p>
              </div>
              <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4 mt-4">
                <h3 className="text-sm font-medium text-content dark:text-content-dark mb-2">
                  {t('Key definitions')}
                </h3>
                <ul className="list-disc pl-6 space-y-1 text-sm text-content-secondary dark:text-content-secondary-dark">
                  <li>{t('"User" means any person or entity that accesses or uses the Service.')}</li>
                  <li>{t('"Account" means a registered user profile with authentication credentials.')}</li>
                  <li>{t('"Subscription" means a paid recurring plan billed monthly or yearly.')}</li>
                  <li>{t('"User Content" means text, images, files, feedback, or other material you submit.')}</li>
                  <li>
                    {t(
                      '"Travel Provider" means airlines, hotels, transport operators, and other third-party suppliers.'
                    )}
                  </li>
                </ul>
              </div>
            </section>

            <section id="travel-services-disclaimer" className={sectionClassName} style={{ animationDelay: '0.015s' }}>
              <SectionHeading id="travel-services-disclaimer" icon={Plane}>
                {t('Travel Services Disclaimer')}
              </SectionHeading>
              <ul className="list-disc pl-6 space-y-2 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <li>
                  {t(
                    'TripRadar is a travel-planning and booking-assistance platform, not an airline, hotel, or transport carrier.'
                  )}
                </li>
                <li>
                  {t(
                    'Travel services are fulfilled by Travel Providers under their own terms, fare rules, baggage policies, cancellation rules, refund rules, and service conditions.'
                  )}
                </li>
                <li>
                  {t(
                    'You are responsible for passports, visas, insurance, local legal compliance, and timely check-in/arrival.'
                  )}
                </li>
                <li>
                  {t(
                    'To the maximum extent permitted by law, TripRadar is not responsible for provider-side schedule changes, force majeure events, strikes, overbooking, or provider insolvency.'
                  )}
                </li>
              </ul>
            </section>

            <section id="accounts-and-security" className={sectionClassName} style={{ animationDelay: '0.03s' }}>
              <SectionHeading id="accounts-and-security" icon={Shield}>
                {t('Accounts and Security')}
              </SectionHeading>
              <ul className="list-disc pl-6 space-y-2 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <li>{t('You must provide accurate account information and keep it up to date.')}</li>
                <li>
                  {t(
                    'You are responsible for all activity under your credentials unless caused by our security breach.'
                  )}
                </li>
                <li>{t('You must notify us promptly if you suspect unauthorized access to your account.')}</li>
                <li>
                  {t(
                    'Passwords must be at least 8 characters and should include mixed character types. Use MFA where available.'
                  )}
                </li>
                <li>
                  {t('You may not impersonate another person or use another user account without authorization.')}
                </li>
                <li>
                  {t(
                    'Separate personal and work accounts are permitted when each account is used lawfully and transparently.'
                  )}
                </li>
              </ul>
            </section>

            <section id="acceptable-use" className={sectionClassName} style={{ animationDelay: '0.045s' }}>
              <SectionHeading id="acceptable-use" icon={Ban}>
                {t('Acceptable Use and Restrictions')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>{t('You agree not to:')}</p>
                <ul className="list-disc pl-6 space-y-2">
                  <li>
                    {t(
                      'Violate laws, sanctions rules, intellectual property rights, privacy rights, or contractual obligations.'
                    )}
                  </li>
                  <li>
                    {t(
                      'Upload malware, phishing content, or harmful code, or interfere with platform security controls.'
                    )}
                  </li>
                  <li>
                    {t('Scrape, crawl, or harvest data at scale except as explicitly permitted by us in writing.')}
                  </li>
                  <li>{t('Bypass authentication, access controls, rate limits, or technical restrictions.')}</li>
                  <li>
                    {t(
                      'Reverse engineer or attempt to extract source code, except where law permits despite this restriction.'
                    )}
                  </li>
                  <li>{t('Use the Service in a way that degrades reliability for other users or our providers.')}</li>
                </ul>
              </div>
            </section>

            <section id="subscriptions" className={sectionClassName} style={{ animationDelay: '0.06s' }}>
              <SectionHeading id="subscriptions" icon={CreditCard}>
                {t('Subscriptions, Auto-Renewal, Cancellation, and Refunds')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'Paid plans renew automatically at the end of each billing cycle unless you cancel before renewal. Billing cycle length depends on the selected plan (monthly or yearly).'
                  )}
                </p>
                <p>
                  {t(
                    'Before checkout, we disclose renewal terms, billing frequency, and cancellation mechanics. By purchasing, you authorize recurring charges under your selected plan.'
                  )}
                </p>
                <p>
                  {t(
                    'You can cancel anytime from account billing settings or by contacting support. Cancellation applies at the end of the current paid period unless otherwise required by law.'
                  )}
                </p>
                <p>
                  {t(
                    'Refund policy: first-time subscriptions are eligible for a 30-day money-back guarantee from initial purchase date unless misuse, fraud, or legal restrictions apply. Outside that guarantee window, refunds are not provided except where required by law.'
                  )}
                </p>
                <p>
                  {t(
                    'If we suspend or terminate your account for serious Terms violations, active subscriptions may be canceled immediately and refunds may be denied to the extent allowed by law.'
                  )}
                </p>
              </div>
            </section>

            <section id="intellectual-property" className={sectionClassName} style={{ animationDelay: '0.075s' }}>
              <SectionHeading id="intellectual-property" icon={FileText}>
                {t('Intellectual Property and User Content')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'The Service, including software, design, and branding, is owned by TripRadar or its licensors and protected by applicable intellectual property laws.'
                  )}
                </p>
                <p>
                  {t(
                    'You retain ownership of User Content. You grant TripRadar a non-exclusive, worldwide, royalty-free license to host, process, reproduce, and adapt that content only as needed to provide, secure, maintain, and improve the Service.'
                  )}
                </p>
                <p>
                  {t(
                    'This license ends when your content is deleted or your account is closed, except for legal retention obligations, incident forensics, and limited backup copies kept for system resilience.'
                  )}
                </p>
              </div>
            </section>

            <section id="suspension-termination" className={sectionClassName} style={{ animationDelay: '0.09s' }}>
              <SectionHeading id="suspension-termination" icon={UserX}>
                {t('Suspension, Termination, and Data Handling')}
              </SectionHeading>
              <ul className="list-disc pl-6 space-y-2 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <li>
                  {t(
                    'We may suspend or terminate access for fraud, abuse, legal violations, security threats, or repeated Terms breaches.'
                  )}
                </li>
                <li>{t('You may stop using the Service and close your account at any time.')}</li>
                <li>
                  {t(
                    'After account closure, we delete or anonymize personal data according to our Privacy Policy retention schedule, except where legal obligations require continued storage.'
                  )}
                </li>
                <li>
                  {t(
                    'We may retain limited records needed for audit, billing disputes, fraud prevention, and legal compliance.'
                  )}
                </li>
              </ul>
            </section>

            <section id="disclaimers-of-warranties" className={sectionClassName} style={{ animationDelay: '0.105s' }}>
              <SectionHeading id="disclaimers-of-warranties" icon={AlertTriangle}>
                {t('Disclaimers of Warranties')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'The Service is provided "AS IS" and "AS AVAILABLE." To the maximum extent permitted by law, we disclaim all implied warranties, including merchantability, fitness for a particular purpose, and non-infringement.'
                  )}
                </p>
                <p>
                  {t(
                    'We do not warrant uninterrupted availability, complete accuracy of third-party travel content, or that third-party links/services will remain continuously available.'
                  )}
                </p>
              </div>
            </section>

            <section
              id="limitation-of-liability"
              className={sectionClassName}
            >
              <SectionHeading
                id="limitation-of-liability"
                icon={AlertTriangle}
              >
                {t('Limitation of Liability')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'To the maximum extent permitted by law, TripRadar and its affiliates are not liable for indirect, incidental, special, consequential, exemplary, or punitive damages, or for loss of profits, revenues, data, goodwill, or business opportunities.'
                  )}
                </p>
                <p>
                  {t(
                    'Our aggregate liability for all claims arising out of or related to the Service is limited to the greater of: (a) amounts you paid to TripRadar in the 12 months before the claim, or (b) USD 100.'
                  )}
                </p>
              </div>
              <div className="rounded-lg border border-outline dark:border-outline-dark bg-surface-accent dark:bg-surface-accent-dark p-4 mt-4">
                <p className="text-sm text-content-secondary dark:text-content-secondary-dark">
                  <strong>{t('Important:')}</strong>{' '}
                  {t(
                    'Some jurisdictions do not allow certain liability exclusions. In those jurisdictions, these limits apply only to the extent legally permitted.'
                  )}
                </p>
              </div>
            </section>

            <section id="copyright-complaints" className={sectionClassName} style={{ animationDelay: '0.135s' }}>
              <SectionHeading id="copyright-complaints" icon={Gavel}>
                {t('Copyright and Abuse Complaints')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'If you believe content on the Service infringes copyright or is unlawful, send a notice to support@tripradar.io with enough detail for us to identify and evaluate the material.'
                  )}
                </p>
                <ul className="list-disc pl-6 space-y-2">
                  <li>
                    {t('Include your contact details, the relevant URL/location, and a description of the issue.')}
                  </li>
                  <li>{t('For copyright complaints, include ownership basis and a good-faith statement.')}</li>
                  <li>
                    {t('We may remove content, restrict accounts, or request additional information to investigate.')}
                  </li>
                </ul>
              </div>
            </section>

            <section id="governing-law" className={sectionClassName} style={{ animationDelay: '0.15s' }}>
              <SectionHeading id="governing-law" icon={Globe}>
                {t('Governing Law and Disputes')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'These Terms are governed by the laws of the State of California, United States, excluding conflict-of-law principles.'
                  )}
                </p>
                <p>
                  {t(
                    'Unless applicable law requires otherwise, courts located in California have exclusive jurisdiction over disputes arising from these Terms or the Service.'
                  )}
                </p>
                <p>
                  {t(
                    'Nothing in these Terms limits mandatory consumer rights under the laws of your country of residence, including rights available in the EEA or UK.'
                  )}
                </p>
              </div>
            </section>

            <section id="sanctions-compliance" className={sectionClassName} style={{ animationDelay: '0.165s' }}>
              <SectionHeading id="sanctions-compliance" icon={Globe}>
                {t('Sanctions and Export Compliance')}
              </SectionHeading>
              <div className="space-y-4 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <p>
                  {t(
                    'You may not use the Service if you are located in, ordinarily resident in, or acting on behalf of persons in jurisdictions prohibited by applicable sanctions or export-control laws.'
                  )}
                </p>
                <p>
                  {t(
                    'You represent that your use of the Service complies with all applicable sanctions, trade, and export restrictions.'
                  )}
                </p>
              </div>
            </section>

            <section id="notices-and-changes" className={sectionClassName} style={{ animationDelay: '0.18s' }}>
              <SectionHeading id="notices-and-changes" icon={Clock}>
                {t('Notices and Changes')}
              </SectionHeading>
              <ul className="list-disc pl-6 space-y-2 leading-relaxed text-content-secondary dark:text-content-secondary-dark">
                <li>{t('We may provide notices by email, in-product messaging, or posting on our website.')}</li>
                <li>
                  {t(
                    'Material Terms changes are announced at least 30 days before they take effect when legally required.'
                  )}
                </li>
                <li>
                  {t(
                    'If you do not agree with updated Terms, you may stop using the Service and cancel your Subscription before the effective date.'
                  )}
                </li>
                <li>{t('Continued use after the effective date means you accept the revised Terms.')}</li>
              </ul>
            </section>

            <section id="contact-us" className={sectionClassName} style={{ animationDelay: '0.195s' }}>
              <SectionHeading id="contact-us" icon={Mail}>
                {t('Contact Us')}
              </SectionHeading>

              <p className="leading-relaxed text-content-secondary dark:text-content-secondary-dark mb-6">
                {t('If you have questions about these Terms, contact us through one of the channels below.')}
              </p>

              <div className="space-y-4">
                <div className="flex items-center space-x-3">
                  <Mail className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">
                      {t('General legal support:')}
                    </span>
                    <a
                      href="mailto:support@tripradar.io"
                      className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark ml-2"
                    >
                      support@tripradar.io
                    </a>
                  </div>
                </div>

                <div className="flex items-center space-x-3">
                  <Mail className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">
                      {t('Privacy and data requests:')}
                    </span>
                    <a
                      href="mailto:privacy@tripradar.io"
                      className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark ml-2"
                    >
                      privacy@tripradar.io
                    </a>
                  </div>
                </div>

                <div className="flex items-center space-x-3">
                  <HelpCircle className="h-5 w-5 text-content-secondary dark:text-content-secondary-dark" />
                  <div>
                    <span className="font-medium text-content dark:text-content-dark">{t('Help Center:')}</span>
                    <Link to="/help" className="font-medium underline text-content dark:text-content-dark hover:text-content-secondary dark:hover:text-content-secondary-dark ml-2">
                      {t('Visit our Help Center')}
                    </Link>
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
