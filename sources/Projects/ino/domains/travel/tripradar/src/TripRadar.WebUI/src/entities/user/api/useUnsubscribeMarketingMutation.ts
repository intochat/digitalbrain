import { useMutation } from '@tanstack/react-query';
import { profileApi, type UnsubscribeMarketingParams } from './profileApi';

export const useUnsubscribeMarketingMutation = () => {
  return useMutation({
    mutationFn: (params: UnsubscribeMarketingParams) => profileApi.unsubscribeFromMarketingEmails(params),
  });
};
