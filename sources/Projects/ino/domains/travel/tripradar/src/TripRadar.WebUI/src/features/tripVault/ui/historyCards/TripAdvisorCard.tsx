import { MapPin, Star, ExternalLink } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
  truncateText,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface TripAdvisorCardProps {
  item: TripHistoryItem;
}

interface ParsedTripAdvisorItem {
  title: string;
  rating: number | null;
  reviewCount: number | null;
  rank: string | null;
  type: string | null;
  address: string | null;
  thumbnail: string | null;
  price: string | null;
  link: string | null;
}

const parseTripAdvisorItem = (place: Record<string, unknown>): ParsedTripAdvisorItem => {
  const images = getArray(place, 'images');
  const firstImage = images ? (images.find(image => typeof image === 'string') as string | undefined) : undefined;

  return {
    title: getString(place, 'title') ?? getString(place, 'name') ?? 'Unknown Place',
    rating: getNumber(place, 'rating'),
    reviewCount: getNumber(place, 'reviews') ?? getNumber(place, 'num_reviews') ?? getNumber(place, 'review_count'),
    rank: getString(place, 'ranking') ?? getString(place, 'rank'),
    type: getString(place, 'place_type') ?? getString(place, 'type') ?? getString(place, 'category'),
    address: getString(place, 'location') ?? getString(place, 'address') ?? getString(place, 'location_string'),
    thumbnail:
      getString(place, 'thumbnail') ?? getString(place, 'photo', 'images', 'small', 'url') ?? firstImage ?? null,
    price: getString(place, 'price') ?? getString(place, 'price_level'),
    link: getString(place, 'link') ?? getString(place, 'web_url') ?? getString(place, 'website'),
  };
};

const parseTripAdvisorData = (data: Record<string, unknown>): ParsedTripAdvisorItem[] => {
  const placesList =
    getArray(data, 'places') ??
    getArray(data, 'results') ??
    getArray(data, 'data') ??
    getArray(data, 'attractions') ??
    getArray(data, 'hotels') ??
    [];

  if (placesList.length > 0) {
    return placesList.slice(0, 5).map(item => parseTripAdvisorItem(item as Record<string, unknown>));
  }

  const placeResult = getObject(data, 'place_result') ?? getObject(data, 'placeResult');
  if (placeResult) {
    return [parseTripAdvisorItem(placeResult)];
  }

  if (getString(data, 'name') || getString(data, 'title')) {
    return [parseTripAdvisorItem(data)];
  }

  return [];
};

export const TripAdvisorCard = ({ item }: TripAdvisorCardProps) => {
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const places = parseTripAdvisorData(data);

  if (places.length === 0) {
    return (
      <p className="pt-0.5 text-xs text-content-secondary dark:text-content-secondary-dark italic">
        No TripAdvisor results found.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      {places.map((place, index) => (
        <div
          key={`ta-place-${index}`}
          className="flex items-start gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
        >
          <ResultImage src={place.thumbnail} alt={place.title} variant="hotel" className="h-12 w-12" />

          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 justify-between">
              <p className="text-xs font-medium text-content dark:text-content-dark truncate">{place.title}</p>
              {place.link && (
                <a
                  href={place.link}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-content-secondary hover:text-primary transition-colors"
                >
                  <ExternalLink className="h-3 w-3" />
                </a>
              )}
            </div>

            <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark flex-wrap mt-0.5">
              {place.rating != null && (
                <span className="flex items-center gap-0.5 text-amber-600 dark:text-amber-400 font-medium">
                  <Star className="h-2.5 w-2.5 fill-current" />
                  {place.rating}
                </span>
              )}
              {place.reviewCount != null && <span>({place.reviewCount.toLocaleString()})</span>}
              {place.price && <span>· {place.price}</span>}
              {place.type && <span>· {place.type}</span>}
            </div>

            {place.rank && (
              <p className="text-[10px] text-emerald-600 dark:text-emerald-400 mt-0.5 truncate">{place.rank}</p>
            )}

            {place.address && (
              <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark mt-0.5 truncate">
                <MapPin className="h-3 w-3 inline mr-0.5 opacity-70" />
                {truncateText(place.address, 60)}
              </p>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};
