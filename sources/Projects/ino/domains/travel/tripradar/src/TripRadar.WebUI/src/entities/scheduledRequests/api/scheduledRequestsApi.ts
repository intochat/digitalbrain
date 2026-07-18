import { apiClient } from 'shared/api';
import type {
  AirportSuggestionItem,
  CreateScheduledQueryResponse,
  CreateScheduledRequestPayload,
  GetAirportSuggestionsResponse,
  GetScheduledExecutionSearchTypesResponse,
  GetScheduledExecutionsResponse,
  UpdateScheduledExecutionConfigurationRequest,
  UpdateScheduledExecutionQueryRequest,
} from './types';

const SCHEDULED_QUERIES_BASE_PATH = '/api/v1/scheduled-queries';
const SCHEDULED_EXECUTIONS_BASE_PATH = '/api/v1/scheduled-executions';
const SEARCH_BASE_PATH = '/api/v1/search';

const encodePathSegment = (value: string): string => encodeURIComponent(value);

export const scheduledRequestsApi = {
  searchAirports: async (query: string, limit = 8): Promise<AirportSuggestionItem[]> => {
    const encodedQuery = encodeURIComponent(query.trim());
    const response = await apiClient.get<GetAirportSuggestionsResponse>(
      `${SEARCH_BASE_PATH}/airports?query=${encodedQuery}&limit=${limit}`
    );
    return response.airports ?? [];
  },

  getScheduledExecutions: async (): Promise<GetScheduledExecutionsResponse> => {
    return apiClient.get(SCHEDULED_EXECUTIONS_BASE_PATH);
  },

  getScheduledExecutionSearchTypes: async (): Promise<GetScheduledExecutionSearchTypesResponse> => {
    return apiClient.get(`${SCHEDULED_EXECUTIONS_BASE_PATH}/search-types`);
  },

  createScheduledRequest: async (request: CreateScheduledRequestPayload): Promise<CreateScheduledQueryResponse> => {
    return apiClient.post(`${SCHEDULED_QUERIES_BASE_PATH}/${request.queryType}`, request.payload);
  },

  updateScheduledExecutionConfiguration: async (
    uniqueId: string,
    configuration: UpdateScheduledExecutionConfigurationRequest
  ): Promise<void> => {
    return apiClient.patch(
      `${SCHEDULED_EXECUTIONS_BASE_PATH}/${encodePathSegment(uniqueId)}/configuration`,
      configuration
    );
  },

  updateScheduledExecutionQuery: async (
    uniqueId: string,
    request: UpdateScheduledExecutionQueryRequest
  ): Promise<void> => {
    return apiClient.patch(`${SCHEDULED_EXECUTIONS_BASE_PATH}/${encodePathSegment(uniqueId)}/query`, request);
  },

  deleteScheduledExecution: async (uniqueId: string): Promise<void> => {
    return apiClient.delete(`${SCHEDULED_EXECUTIONS_BASE_PATH}/${encodePathSegment(uniqueId)}`);
  },
};

