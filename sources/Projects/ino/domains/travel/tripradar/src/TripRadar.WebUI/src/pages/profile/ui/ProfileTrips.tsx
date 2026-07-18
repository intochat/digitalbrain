import { useSubscriptionQuery } from 'entities/payment/api';
import { usePrivacyModeQuery } from 'entities/preferences/api';
import { TripVaultSection } from 'features/tripVault';
import { ProfileLayout } from './ProfileLayout';

export const ProfileTrips = () => {
  const subscriptionQuery = useSubscriptionQuery();
  const privacyModeQuery = usePrivacyModeQuery();
  const isPaidUser =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    subscriptionQuery.data.tierType.toLowerCase() !== 'basic';
  const noTraceEnabled = isPaidUser && !privacyModeQuery.isError && (privacyModeQuery.data?.enabled ?? false);

  return (
    <ProfileLayout>
      <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
        <TripVaultSection isPaidUser={isPaidUser} noTraceEnabled={noTraceEnabled} />
      </div>
    </ProfileLayout>
  );
};
