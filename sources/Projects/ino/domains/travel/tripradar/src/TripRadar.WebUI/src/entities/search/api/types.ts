export interface LocationSuggestionItem {
  locationId: number;
  name: string;
  canonicalName: string;
  countryCode: string;
  targetType: string;
  latitude: number | null;
  longitude: number | null;
}

export interface GetLocationSuggestionsResponse {
  locations: LocationSuggestionItem[];
}
