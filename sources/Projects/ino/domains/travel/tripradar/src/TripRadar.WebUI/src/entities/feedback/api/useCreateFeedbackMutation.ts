import { useMutation, useQueryClient } from '@tanstack/react-query';
import { feedbackApi } from './feedbackApi';
import type { CreateFeedbackRequest } from './types';

export const useCreateFeedbackMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateFeedbackRequest) => feedbackApi.createFeedback(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['feedbacks', 'user'] });
      queryClient.invalidateQueries({ queryKey: ['feedbacks', 'all'] });
    },
  });
};
