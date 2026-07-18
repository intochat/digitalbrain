import { useFrontendLanguage } from 'app/providers';
import { useSubscriptionQuery } from 'entities/payment/api';
import { useProfileQuery } from 'entities/user/api';
import { capitalize } from './billingUtils';

interface UseTierInfoOptions {
  enabled?: boolean;
}

type QueryError = Error & {
  code?: string;
  response?: {
    status?: number;
  };
};

const isMissingSubscriptionError = (error: unknown): boolean => {
  const queryError = error as QueryError | null | undefined;
  return queryError?.code === 'SUBSCRIPTION_NOT_FOUND' || queryError?.response?.status === 404;
};

export const useTierInfo = (options?: UseTierInfoOptions) => {
  const { t } = useFrontendLanguage();
  const enabled = options?.enabled ?? true;
  const {
    data: profile,
    isLoading: profileLoading,
    error: profileError,
    refetch: refetchProfile,
  } = useProfileQuery({ enabled });
  const {
    data: subscription,
    isLoading: subLoading,
    error: subError,
    refetch: refetchSubscription,
  } = useSubscriptionQuery({ enabled });
  const subscriptionMissing = isMissingSubscriptionError(subError);

  const tierName = subscription?.tierType || profile?.tierName || 'basic';
  const isBasicTier = tierName.toLowerCase() === 'basic';
  const localizedTierName = t(capitalize(tierName));
  const isLoading = subLoading || profileLoading;
  const error = subscriptionMissing ? profileError : (subError ?? profileError);
  const refetch = () => {
    refetchProfile();
    refetchSubscription();
  };

  return { tierName, isBasicTier, localizedTierName, subscription, profile, isLoading, error, refetch };
};
