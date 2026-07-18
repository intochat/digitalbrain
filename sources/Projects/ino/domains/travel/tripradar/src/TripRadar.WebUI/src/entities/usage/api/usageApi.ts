import { apiClient } from 'shared/api';
import type { GetUsageEventsResponse, UsageEventsQueryParams } from './types';

const USAGE_BASE_PATH = '/api/v1.0/usage';

export const usageApi = {
  getUsageEvents: async (query: UsageEventsQueryParams = {}): Promise<GetUsageEventsResponse> => {
    const searchParams = new URLSearchParams();

    if (query.from) {
      searchParams.set('from', query.from);
    }

    if (query.to) {
      searchParams.set('to', query.to);
    }

    if (query.groupBy) {
      searchParams.set('groupBy', query.groupBy);
    }

    if (query.serviceType) {
      searchParams.set('serviceType', query.serviceType);
    }

    if (query.tripVaultUniqueId) {
      searchParams.set('tripVaultUniqueId', query.tripVaultUniqueId);
    }

    if (query.source) {
      searchParams.set('source', query.source);
    }

    if (query.page) {
      searchParams.set('page', String(query.page));
    }

    if (query.pageSize) {
      searchParams.set('pageSize', String(query.pageSize));
    }

    const queryString = searchParams.toString();
    const endpoint = queryString ? `${USAGE_BASE_PATH}/events?${queryString}` : `${USAGE_BASE_PATH}/events`;
    return apiClient.get(endpoint);
  },
};
