export interface TripVaultItem {
  uniqueId: string;
  name: string;
  description?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  itemsCount: number;
  createdOn: string;
}

export interface CreateTripVaultRequest {
  name: string;
  description?: string | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface UpdateTripVaultRequest {
  name: string;
  description?: string | null;
  startDate?: string | null;
  endDate?: string | null;
}

export type ServiceType =
  | 'Event'
  | 'Flight'
  | 'Hotel'
  | 'LocalPlaces'
  | 'Maps'
  | 'PlaceReview'
  | 'FlightExplore'
  | 'TripAdvisorSearch'
  | 'TripAdvisorPlace'
  | 'OpenTableReview'
  | 'YelpSearch'
  | 'YelpPlace'
  | 'YelpReviews'
  | 'YelpPlaceFullMenu'
  | 'MapsDirections'
  | 'MapsPlaceResults'
  | 'GoogleLightSearch'
  | string;

export interface TripHistoryItem {
  uniqueId: string;
  serviceType: ServiceType;
  queryParametersJson: string;
  startDateTime?: string | null;
  endDateTime?: string | null;
  resultSummary?: string | null;
  createdOn: string;
}

export interface TripQueryHistoryResponse {
  items: TripHistoryItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
