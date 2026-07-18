import type { UsageSourceType, UsageTimelinePointResponse } from 'entities/usage/api';

export interface DaySourceBreakdown {
  source: UsageSourceType;
  tokens: number;
  percentage: number;
}

export interface DayTimelinePoint {
  date: string;
  totalTokens: number;
  eventsCount: number;
  breakdown: DaySourceBreakdown[];
}

const USAGE_SERVICE_TYPE_LABELS: Record<string, string> = {
  Event: 'Events',
  Flight: 'Flights',
  Hotel: 'Hotels',
  LocalPlaces: 'Local Places',
  Maps: 'Maps',
  PlaceReview: 'Place Reviews',
  FlightExplore: 'Flight Searches',
  FlightPriceCalendar: 'Flight Price Calendar',
  TripAdvisorSearch: 'TripAdvisor Searches',
  TripAdvisorPlace: 'TripAdvisor Places',
  OpenTableReview: 'OpenTable Reviews',
  GoogleVideoSearch: 'Google Video Searches',
  YelpSearch: 'Yelp Searches',
  YelpPlace: 'Yelp Places',
  YelpReviews: 'Yelp Reviews',
  YelpPlaceFullMenu: 'Yelp Full Menus',
  MapsDirections: 'Routes',
  MapsPlaceResults: 'Map Places',
  GoogleLightSearch: 'Google Searches',
};

export const resolveLocale = (language: string): string => {
  return language === 'ru' ? 'ru-RU' : 'en-US';
};

export const toDateInputValue = (date: Date): string => {
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const day = String(date.getUTCDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

export const buildUtcDateRange = (days: number): { from: string; to: string } => {
  const now = new Date();
  const utcNow = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
  const from = new Date(utcNow);
  from.setUTCDate(from.getUTCDate() - (days - 1));

  return {
    from: toDateInputValue(from),
    to: toDateInputValue(utcNow),
  };
};

export const clampPercent = (value: number): number => {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.max(0, Math.min(100, Math.round(value)));
};

export const formatDate = (value: string, locale: string): string => {
  const date = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleDateString(locale, {
    month: 'short',
    day: 'numeric',
  });
};

export const formatTooltipDate = (value: string, locale: string): string => {
  const date = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleDateString(locale, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
};

export const formatDateTime = (value: string, locale: string): string => {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString(locale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
};

export const formatUsageServiceType = (serviceType: string, t: (key: string) => string): string => {
  const labelKey = USAGE_SERVICE_TYPE_LABELS[serviceType];
  if (labelKey) {
    return t(labelKey);
  }

  return serviceType.replace(/([a-z])([A-Z])/g, '$1 $2');
};

export const buildTimelineWithEmptyDays = (
  from: string,
  to: string,
  timeline: UsageTimelinePointResponse[]
): UsageTimelinePointResponse[] => {
  const fromDate = new Date(`${from}T00:00:00Z`);
  const toDate = new Date(`${to}T00:00:00Z`);
  if (Number.isNaN(fromDate.getTime()) || Number.isNaN(toDate.getTime()) || fromDate > toDate) {
    return timeline;
  }

  const byDate = new Map(timeline.map(point => [point.date, point]));
  const normalized: UsageTimelinePointResponse[] = [];
  const cursor = new Date(fromDate);

  while (cursor <= toDate) {
    const key = toDateInputValue(cursor);
    const existing = byDate.get(key);
    normalized.push(
      existing ?? {
        date: key,
        tokensConsumed: 0,
        eventsCount: 0,
      }
    );
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }

  return normalized;
};

export const createSourceTokenMap = (): Record<UsageSourceType, Map<string, number>> => ({
  api: new Map<string, number>(),
  scheduled: new Map<string, number>(),
  telegram: new Map<string, number>(),
  ai: new Map<string, number>(),
});

export const createSourceTotals = (): Record<UsageSourceType, number> => ({
  api: 0,
  scheduled: 0,
  telegram: 0,
  ai: 0,
});
