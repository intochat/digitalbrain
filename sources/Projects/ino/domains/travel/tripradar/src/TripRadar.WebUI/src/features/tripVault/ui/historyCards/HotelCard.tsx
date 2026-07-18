import { Building2 } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  formatPrice,
  formatRating,
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface HotelCardProps {
  item: TripHistoryItem;
}

interface ParsedProperty {
  name: string;
  rating: number | null;
  reviews: number | null;
  price: number | null;
  hotelClass: string | null;
  thumbnail: string | null;
  type: string | null;
}

const parseProperties = (data: Record<string, unknown>): ParsedProperty[] => {
  const properties = getArray(data, 'properties') ?? [];

  return properties.slice(0, 5).map(prop => {
    const p = prop as Record<string, unknown>;
    return {
      name: getString(p, 'name') ?? getString(p, 'title') ?? 'Unknown hotel',
      rating: getNumber(p, 'overallRating') ?? getNumber(p, 'overall_rating') ?? getNumber(p, 'rating'),
      reviews: getNumber(p, 'reviews'),
      price:
        getNumber(p, 'rate_per_night', 'extracted_lowest') ??
        getNumber(p, 'ratePerNight', 'extractedLowest') ??
        getNumber(p, 'totalRate', 'extractedLowest') ??
        getNumber(p, 'total_rate', 'extracted_lowest'),
      hotelClass: getString(p, 'hotelClass') ?? getString(p, 'hotel_class'),
      thumbnail: getString(p, 'images', '0', 'thumbnail') ?? getString(p, 'thumbnail'),
      type: getString(p, 'type'),
    };
  });
};

export const HotelCard = ({ item }: HotelCardProps) => {
  const data = safeParse(item.resultSummary);
  const queryData = safeParse(item.queryParametersJson);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const properties = parseProperties(data);
  const searchInfo = getObject(data, 'searchInformation') ?? getObject(data, 'search_information');
  const totalProperties = searchInfo
    ? (getNumber(searchInfo, 'totalResults') ?? getNumber(searchInfo, 'total_results'))
    : null;

  // Query params for context
  const searchQuery = queryData ? (getString(queryData, 'searchQuery', 'q') ?? null) : null;
  const checkIn = queryData
    ? (getString(queryData, 'advancedParameters', 'checkInDate') ??
      getString(queryData, 'advancedParameters', 'check_in_date') ??
      null)
    : null;
  const checkOut = queryData
    ? (getString(queryData, 'advancedParameters', 'checkOutDate') ??
      getString(queryData, 'advancedParameters', 'check_out_date') ??
      null)
    : null;

  return (
    <div className="space-y-3">
      {/* Search context */}
      <div className="flex items-center gap-3 flex-wrap">
        {searchQuery && (
          <div className="inline-flex items-center gap-2 rounded-lg bg-emerald-50 dark:bg-emerald-500/10 px-3 py-1.5 text-sm font-semibold text-emerald-700 dark:text-emerald-300">
            <Building2 className="h-4 w-4" />
            {searchQuery}
          </div>
        )}
        {checkIn && checkOut && (
          <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
            {checkIn} — {checkOut}
          </span>
        )}
        {totalProperties != null && (
          <span className="text-xs text-content-secondary dark:text-content-secondary-dark">
            {totalProperties} properties found
          </span>
        )}
      </div>

      {/* Properties list */}
      {properties.length > 0 && (
        <div className="space-y-2">
          {properties.map((prop, index) => (
            <div
              key={`hotel-${index}`}
              className="flex items-center gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <ResultImage src={prop.thumbnail} alt={prop.name} variant="hotel" />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-content dark:text-content-dark truncate">{prop.name}</p>
                <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark">
                  {prop.rating != null && <span>{formatRating(prop.rating)}</span>}
                  {prop.reviews != null && <span>({prop.reviews} reviews)</span>}
                  {prop.hotelClass && <span>· {prop.hotelClass}</span>}
                  {prop.type && <span>· {prop.type}</span>}
                </div>
              </div>
              {prop.price != null && (
                <span className="text-sm font-semibold text-content dark:text-content-dark whitespace-nowrap">
                  {formatPrice(prop.price)}
                  <span className="text-[10px] font-normal text-content-secondary dark:text-content-secondary-dark">
                    /night
                  </span>
                </span>
              )}
            </div>
          ))}
        </div>
      )}

      {properties.length === 0 && (
        <p className="pt-0.5 text-xs text-content-secondary dark:text-content-secondary-dark">
          No hotel properties available in the response snapshot.
        </p>
      )}
    </div>
  );
};
