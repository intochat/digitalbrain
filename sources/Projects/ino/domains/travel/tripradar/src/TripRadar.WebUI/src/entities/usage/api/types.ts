export type UsageGroupBy = 'day' | 'week';
export type UsageSourceType = 'api' | 'scheduled' | 'telegram' | 'ai';

export interface UsageEventsQueryParams {
  from?: string;
  to?: string;
  groupBy?: UsageGroupBy;
  serviceType?: string;
  tripVaultUniqueId?: string;
  source?: UsageSourceType;
  page?: number;
  pageSize?: number;
}

export interface UsageSummaryResponse {
  currentUsage: number;
  monthlyLimit: number;
  remainingTokens: number;
}

export interface UsageTimelinePointResponse {
  date: string;
  tokensConsumed: number;
  eventsCount: number;
}

export interface UsageTripVaultResponse {
  uniqueId: string;
  name: string;
}

export interface UsageEventItemResponse {
  uniqueId: string;
  occurredAt: string;
  serviceType: string;
  source: string;
  tokensConsumed: number;
  tripVault?: UsageTripVaultResponse | null;
}

export interface UsagePaginationResponse {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GetUsageEventsResponse {
  summary: UsageSummaryResponse;
  timeline: UsageTimelinePointResponse[];
  events: UsageEventItemResponse[];
  pagination: UsagePaginationResponse;
}
