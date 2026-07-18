import { useQuery } from '@tanstack/react-query';
import { feedbackApi } from './feedbackApi';

export const useUserFeedbacksQuery = () => {
  return useQuery({
    queryKey: ['feedbacks', 'user'],
    queryFn: () => feedbackApi.getUserFeedbacks(),
  });
};
