import type { components } from 'shared/api';

export type ScheduledQueryType = 'events' | 'flights' | 'hotels' | 'local-places';

export interface GetScheduledExecutionSearchTypesResponse {
  searchTypes: string[];
}

export type CreateScheduledEventQueryRequest = components['schemas']['CreateScheduledEventQueryRequest'];
export type CreateScheduledFlightQueryRequest = components['schemas']['CreateScheduledFlightQueryRequest'];
export type CreateScheduledHotelQueryRequest = components['schemas']['CreateScheduledHotelQueryRequest'];
export type CreateScheduledLocalPlacesQueryRequest = components['schemas']['CreateScheduledLocalPlacesQueryRequest'];

export type CreateScheduledRequestPayload =
  | { queryType: 'events'; payload: CreateScheduledEventQueryRequest }
  | { queryType: 'flights'; payload: CreateScheduledFlightQueryRequest }
  | { queryType: 'hotels'; payload: CreateScheduledHotelQueryRequest }
  | { queryType: 'local-places'; payload: CreateScheduledLocalPlacesQueryRequest };

export interface QueryColumn {
  name: string;
  isActive: boolean;
}

export interface AirportSuggestionItem {
  code: string;
  name: string;
  city: string;
  countryCode: string;
  searchAliases?: string | null;
}

export interface GetAirportSuggestionsResponse {
  airports: AirportSuggestionItem[];
}

export interface ScheduledExecutionItem {
  scheduledExecutionUniqueId: string;
  serviceType: string;
  isActive: boolean;
  nextExecutionTime: string;
  schedule: string;
  createdOn: string;
  updatedOn?: string | null;
  requestSummary: string;
  searchQuery?: string | null;
  location?: string | null;
  radius?: number | null;
  departureAirportCode?: string | null;
  departureAirportCity?: string | null;
  departureAirportSearchAliases?: string | null;
  destinationAirportCode?: string | null;
  destinationAirportCity?: string | null;
  destinationAirportSearchAliases?: string | null;
  departureDate?: string | null;
  returnDate?: string | null;
  checkInDate?: string | null;
  checkOutDate?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  additionalParameters?: string | null;
  selectedColumns?: QueryColumn[] | null;
}

export interface GetScheduledExecutionsResponse {
  scheduledExecutions: ScheduledExecutionItem[];
}

export interface CreateScheduledQueryResponse {
  scheduledExecutionUniqueId: string;
}

export interface UpdateScheduledExecutionConfigurationRequest {
  isActive: boolean;
  schedule?: string;
  nextExecutionTime?: string;
}

export interface UpdateScheduledExecutionQueryRequest {
  searchQuery?: string;
  location?: string;
  radius?: number;
  departureAirportCode?: string;
  destinationAirportCode?: string;
  departureDate?: string;
  returnDate?: string;
  checkInDate?: string;
  checkOutDate?: string;
}
