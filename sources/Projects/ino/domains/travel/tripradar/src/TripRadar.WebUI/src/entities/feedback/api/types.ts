import type { components } from 'shared/api';

export type FeedbackCategoryType = components['schemas']['FeedbackCategoryType'];
export type FeedbackCategoryDto = components['schemas']['FeedbackCategoryDto'];
export type CreateFeedbackRequest = components['schemas']['CreateFeedbackRequest'];
export type GetFeedbackResponse = components['schemas']['GetFeedbackResponse'];
export type GetUserFeedbackResponse = components['schemas']['GetUserFeedbackResponse'];
export type GetFeedbackCategoriesResponse = components['schemas']['GetFeedbackCategoriesResponse'];
export type GetAllFeedbacksResponse = components['schemas']['GetAllFeedbacksResponse'];
export type GetAllFeedbacksResponsePaginatedResponse =
  components['schemas']['GetAllFeedbacksResponsePaginatedResponse'];
