import { apiClient } from 'shared/api';
import type {
  GetAllFeedbacksResponsePaginatedResponse,
  CreateFeedbackRequest,
  FeedbackCategoryDto,
  GetFeedbackCategoriesResponse,
  GetUserFeedbackResponse,
} from './types';

const FEEDBACKS_BASE_PATH = '/api/v1/feedbacks';

export const feedbackApi = {
  getFeedbackCategories: async (): Promise<FeedbackCategoryDto[]> => {
    const response = await apiClient.get<GetFeedbackCategoriesResponse>(`${FEEDBACKS_BASE_PATH}/categories`);
    return response.categories ?? [];
  },

  getAllFeedbacks: async (
    pageNumber: number = 1,
    pageSize: number = 100
  ): Promise<GetAllFeedbacksResponsePaginatedResponse> => {
    const searchParams = new URLSearchParams({
      pageNumber: String(pageNumber),
      pageSize: String(pageSize),
    });

    return apiClient.get(`${FEEDBACKS_BASE_PATH}?${searchParams.toString()}`);
  },

  getUserFeedbacks: async (): Promise<GetUserFeedbackResponse[]> => {
    return apiClient.get(`${FEEDBACKS_BASE_PATH}/user`);
  },

  createFeedback: async (request: CreateFeedbackRequest): Promise<GetUserFeedbackResponse> => {
    return apiClient.post(`${FEEDBACKS_BASE_PATH}/user`, request);
  },
};
