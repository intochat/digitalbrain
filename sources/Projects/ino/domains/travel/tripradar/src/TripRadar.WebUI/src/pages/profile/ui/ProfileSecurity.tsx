import { ProfileLayout } from './ProfileLayout';
import { DangerZoneSection } from './security/DangerZoneSection';
import { PasswordSection } from './security/PasswordSection';
import { PrivacySection } from './security/PrivacySection';

export const ProfileSecurity = () => (
  <ProfileLayout>
    <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
      <div className="space-y-8 divide-y divide-outline/40 dark:divide-outline-dark/40 [&>*+*]:pt-8">
        <PasswordSection />
        <PrivacySection />
        <DangerZoneSection />
      </div>
    </div>
  </ProfileLayout>
);
