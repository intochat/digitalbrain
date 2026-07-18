import { useQuery } from '@tanstack/react-query';
import { feedbackApi } from './feedbackApi';

export const useAllFeedbacksQuery = (pageNumber: number = 1, pageSize: number = 100) => {
  return useQuery({
    queryKey: ['feedbacks', 'all', pageNumber, pageSize],
    queryFn: () => feedbackApi.getAllFeedbacks(pageNumber, pageSize),
  });
};
