import type { TripHistoryItem } from 'entities/tripVault';
import { EventCard } from './EventCard';
import { FlightCard } from './FlightCard';
import { GenericCard } from './GenericCard';
import { GoogleSearchCard } from './GoogleSearchCard';
import { HotelCard } from './HotelCard';
import { LocalPlacesCard } from './LocalPlacesCard';
import { MapsCard } from './MapsCard';
import { OpenTableCard } from './OpenTableCard';
import { TripAdvisorCard } from './TripAdvisorCard';
import { YelpCard } from './YelpCard';

interface HistoryItemCardProps {
  item: TripHistoryItem;
}

/** Maps numeric C# enum values to their string names for when the API returns numbers. */
const SERVICE_TYPE_MAP: Record<number, string> = {
  1: 'Event',
  2: 'Flight',
  3: 'Hotel',
  4: 'LocalPlaces',
  5: 'Maps',
  6: 'PlaceReview',
  7: 'FlightExplore',
  8: 'TripAdvisorSearch',
  9: 'TripAdvisorPlace',
  10: 'OpenTableReview',
  11: 'GoogleVideoSearch',
  12: 'YelpSearch',
  13: 'YelpPlace',
  14: 'YelpReviews',
  15: 'YelpPlaceFullMenu',
  16: 'MapsDirections',
  17: 'MapsPlaceResults',
  18: 'GoogleLightSearch',
};

/** Resolve a serviceType that might be numeric into its string name, then compare. */
const matchType = (type: string, candidates: readonly string[]): boolean => {
  const raw = type as unknown;
  const resolved = typeof raw === 'number' ? (SERVICE_TYPE_MAP[raw] ?? String(raw)) : String(raw);
  const normalised = resolved.toLowerCase();
  return candidates.some(c => c.toLowerCase() === normalised);
};

const FLIGHT_TYPES = ['Flight', 'FlightExplore'] as const;
const HOTEL_TYPES = ['Hotel'] as const;
const OPENTABLE_TYPES = ['OpenTableReview'] as const;
const MAPS_TYPES = ['Maps', 'MapsPlaceResults', 'MapsDirections', 'PlaceReview'] as const;
const EVENT_TYPES = ['Event'] as const;
const LOCAL_PLACES_TYPES = ['LocalPlaces'] as const;
const YELP_TYPES = ['YelpSearch', 'YelpPlace', 'YelpReviews', 'YelpPlaceFullMenu'] as const;
const TRIPADVISOR_TYPES = ['TripAdvisorSearch', 'TripAdvisorPlace'] as const;
const GOOGLE_SEARCH_TYPES = ['GoogleLightSearch'] as const;
const GOOGLE_VIDEO_TYPES = ['GoogleVideoSearch'] as const;

export const HistoryItemCard = ({ item }: HistoryItemCardProps) => {
  const type = item.serviceType;

  if (matchType(type, FLIGHT_TYPES)) {
    return <FlightCard item={item} />;
  }

  if (matchType(type, HOTEL_TYPES)) {
    return <HotelCard item={item} />;
  }

  if (matchType(type, OPENTABLE_TYPES)) {
    return <OpenTableCard item={item} />;
  }

  if (matchType(type, MAPS_TYPES)) {
    return <MapsCard item={item} />;
  }

  if (matchType(type, EVENT_TYPES)) {
    return <EventCard item={item} />;
  }

  if (matchType(type, LOCAL_PLACES_TYPES)) {
    return <LocalPlacesCard item={item} />;
  }

  if (matchType(type, YELP_TYPES)) {
    return <YelpCard item={item} />;
  }

  if (matchType(type, TRIPADVISOR_TYPES)) {
    return <TripAdvisorCard item={item} />;
  }

  if (matchType(type, GOOGLE_SEARCH_TYPES) || matchType(type, GOOGLE_VIDEO_TYPES)) {
    return <GoogleSearchCard item={item} />;
  }

  return <GenericCard item={item} />;
};
