import { MapPin, Search } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  formatRating,
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
  truncateText,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface LocalPlacesCardProps {
  item: TripHistoryItem;
}

interface ParsedPlace {
  title: string;
  rating: number | null;
  reviews: number | null;
  type: string | null;
  address: string | null;
  thumbnail: string | null;
  phone: string | null;
  priceLevel: string | null;
  serviceOptions: string[];
}

const parsePlaces = (data: Record<string, unknown>): ParsedPlace[] => {
  const localResults = getArray(data, 'localResults') ?? getArray(data, 'local_results') ?? [];

  return localResults
    .slice(0, 5)
    .map(place => {
      const p = place as Record<string, unknown>;
      const serviceOpts = getObject(p, 'serviceOptions') ?? getObject(p, 'service_options');

      return {
        title: getString(p, 'title') ?? getString(p, 'name') ?? '',
        rating: getNumber(p, 'rating'),
        reviews: getNumber(p, 'reviews'),
        type: getString(p, 'type') ?? getString(p, 'category'),
        address: getString(p, 'address'),
        thumbnail: getString(p, 'thumbnail'),
        phone: getString(p, 'phone'),
        priceLevel: getString(p, 'price') ?? getString(p, 'priceLevel') ?? getString(p, 'price_level'),
        serviceOptions: serviceOpts
          ? Object.entries(serviceOpts)
              .filter(([, value]) => value === true)
              .map(([key]) => key.replace(/_/g, ' ').replace(/\b\w/g, ch => ch.toUpperCase()))
          : [],
      };
    })
    .filter(place => place.title.length > 0);
};

export const LocalPlacesCard = ({ item }: LocalPlacesCardProps) => {
  const data = safeParse(item.resultSummary);
  const queryData = safeParse(item.queryParametersJson);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const places = parsePlaces(data);
  const searchInfo = getObject(data, 'searchInformation') ?? getObject(data, 'search_information');
  const totalResults = searchInfo
    ? (getNumber(searchInfo, 'totalResults') ?? getNumber(searchInfo, 'total_results'))
    : null;
  const searchQuery = queryData ? (getString(queryData, 'searchQuery', 'q') ?? null) : null;

  return (
    <div className="space-y-3">
      {/* Search context */}
      <div className="flex items-center gap-3 flex-wrap">
        {searchQuery && (
          <div className="inline-flex items-center gap-2 rounded-lg bg-teal-50 dark:bg-teal-500/10 px-3 py-1.5 text-sm font-semibold text-teal-700 dark:text-teal-300">
            <Search className="h-4 w-4" />
            {searchQuery}
          </div>
        )}
        {totalResults != null && (
          <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
            {totalResults} results
          </span>
        )}
      </div>

      {/* Places list */}
      {places.length > 0 && (
        <div className="space-y-2">
          {places.map((place, index) => (
            <div
              key={`local-place-${index}`}
              className="flex items-start gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <ResultImage src={place.thumbnail} alt={place.title} variant="place" />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-content dark:text-content-dark truncate">{place.title}</p>
                <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark flex-wrap">
                  {place.rating != null && <span>{formatRating(place.rating)}</span>}
                  {place.reviews != null && <span>({place.reviews})</span>}
                  {place.type && <span>· {place.type}</span>}
                  {place.priceLevel && <span>· {place.priceLevel}</span>}
                </div>
                {place.address && (
                  <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark mt-0.5 truncate">
                    <MapPin className="h-3 w-3 inline mr-0.5" />
                    {truncateText(place.address, 50)}
                  </p>
                )}
                {place.serviceOptions.length > 0 && (
                  <div className="flex flex-wrap gap-1 mt-1">
                    {place.serviceOptions.slice(0, 3).map(opt => (
                      <span
                        key={opt}
                        className="inline-flex rounded-full bg-surface-accent/60 dark:bg-surface-accent-dark/40 px-1.5 py-0 text-[10px] text-content-secondary dark:text-content-secondary-dark"
                      >
                        {opt}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {places.length === 0 && (
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
          No local places available in the response snapshot.
        </p>
      )}
    </div>
  );
};
