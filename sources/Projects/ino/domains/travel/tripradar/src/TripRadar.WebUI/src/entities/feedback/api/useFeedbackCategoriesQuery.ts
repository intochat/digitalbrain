import { useQuery } from '@tanstack/react-query';
import { feedbackApi } from './feedbackApi';

export const useFeedbackCategoriesQuery = (enabled: boolean = true) => {
  return useQuery({
    queryKey: ['feedbacks', 'categories'],
    queryFn: () => feedbackApi.getFeedbackCategories(),
    enabled,
  });
};
