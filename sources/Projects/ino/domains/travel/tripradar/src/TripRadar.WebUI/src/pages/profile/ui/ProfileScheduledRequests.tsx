import { ScheduledRequestsSection } from 'features/scheduledRequests';
import { ProfileLayout } from './ProfileLayout';

export const ProfileScheduledRequests = () => (
  <ProfileLayout>
    <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
      <ScheduledRequestsSection />
    </div>
  </ProfileLayout>
);
