import { MapPin, Clock, Navigation } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
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

interface MapsCardProps {
  item: TripHistoryItem;
}

export const MapsCard = ({ item }: MapsCardProps) => {
  const { t } = useFrontendLanguage();
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  // SerpApi wraps place info under "place_info" or it may be at top level
  const placeInfo =
    getObject(data, 'place_info') ??
    getObject(data, 'placeInfo') ??
    getObject(data, 'place_results') ??
    getObject(data, 'placeResults');

  // Place name - try nested first, then top-level
  const title =
    getString(placeInfo ?? data, 'title') ??
    getString(placeInfo ?? data, 'name') ??
    getString(data, 'title') ??
    getString(data, 'name');

  const rating = getNumber(placeInfo ?? data, 'rating') ?? getNumber(data, 'rating');
  const reviewCount =
    getNumber(placeInfo ?? data, 'reviews') ??
    getNumber(placeInfo ?? data, 'review_count') ??
    getNumber(placeInfo ?? data, 'reviewCount') ??
    getNumber(data, 'reviews_count');
  const address = getString(placeInfo ?? data, 'address') ?? getString(data, 'address');
  const phone = getString(placeInfo ?? data, 'phone') ?? getString(data, 'phone');
  const website = getString(placeInfo ?? data, 'website') ?? getString(data, 'website');
  const description = getString(placeInfo ?? data, 'description') ?? getString(data, 'description');
  const typeArray = getArray(placeInfo ?? data, 'type')
    ?.filter(value => typeof value === 'string')
    .map(value => value as string);
  const typeFromArray = typeArray && typeArray.length > 0 ? typeArray.slice(0, 2).join(', ') : null;
  const type =
    getString(placeInfo ?? data, 'type') ??
    getString(placeInfo ?? data, 'category') ??
    getString(data, 'type') ??
    typeFromArray;
  const priceLevel =
    getString(placeInfo ?? data, 'price_level') ??
    getString(placeInfo ?? data, 'priceLevel') ??
    getString(data, 'price');
  const thumbnail = getString(placeInfo ?? data, 'thumbnail') ?? getString(data, 'thumbnail');

  // Service options
  const serviceOptions =
    getObject(placeInfo ?? data, 'service_options') ?? getObject(placeInfo ?? data, 'serviceOptions');
  const serviceOptionsList = serviceOptions
    ? Object.entries(serviceOptions)
        .filter(([, value]) => value === true)
        .map(([key]) => key.replace(/_/g, ' '))
        .map(key => key.charAt(0).toUpperCase() + key.slice(1))
    : [];

  // Operating hours
  const operatingHours =
    getObject(placeInfo ?? data, 'operating_hours') ??
    getObject(placeInfo ?? data, 'operatingHours') ??
    getObject(data, 'hours');
  const operatingHoursList = getArray(placeInfo ?? data, 'hours') ?? getArray(data, 'hours');

  // GPS coordinates
  const gps = getObject(placeInfo ?? data, 'gps_coordinates') ?? getObject(placeInfo ?? data, 'gpsCoordinates');
  const lat = gps ? (getNumber(gps, 'latitude') ?? getNumber(gps, 'lat')) : null;
  const lng = gps ? (getNumber(gps, 'longitude') ?? getNumber(gps, 'lng') ?? getNumber(gps, 'lon')) : null;

  // Multiple place results (MapsPlaceResults)
  const placeResults =
    getArray(data, 'place_results') ??
    getArray(data, 'placeResults') ??
    getArray(data, 'local_results') ??
    getArray(data, 'localResults');
  const placeResultsObject = getObject(data, 'place_results') ?? getObject(data, 'placeResults');
  const normalizedPlaceResults =
    placeResults && placeResults.length > 0 ? placeResults : placeResultsObject ? [placeResultsObject] : null;

  // Directions
  const directions = getArray(data, 'directions') ?? getArray(data, 'routes');

  // Reviews
  const reviews = getArray(data, 'reviews') ?? [];

  // Check if there's anything at all to show
  const hasPlaceData = title || address || rating != null;
  const hasPlaceResults = normalizedPlaceResults && normalizedPlaceResults.length > 0;
  const hasDirections = directions && directions.length > 0;
  const hasReviews = (reviews as Array<Record<string, unknown>>).length > 0;

  if (!hasPlaceData && !hasPlaceResults && !hasDirections && !hasReviews) {
    // Try showing search query parameters instead
    const queryData = safeParse(item.queryParametersJson);
    const query =
      getString(queryData ?? {}, 'q') ?? getString(queryData ?? {}, 'query') ?? getString(queryData ?? {}, 'address');

    if (!query) {
      return <GenericCard item={item} />;
    }

    return (
      <div className="text-xs text-content-secondary dark:text-content-secondary-dark">
        <div className="flex items-center gap-2">
          <MapPin className="h-3.5 w-3.5" />
          <span>
            {t('Search:')} <strong>{query}</strong>
          </span>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {/* Place header */}
      {hasPlaceData && (
        <div className="flex items-start gap-3">
          <ResultImage src={thumbnail} alt={title ?? t('Place')} variant="place" className="h-12 w-12" />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <div className="inline-flex items-center gap-2 rounded-lg bg-violet-50 dark:bg-violet-500/10 px-3 py-1.5 text-sm font-semibold text-violet-700 dark:text-violet-300">
                <MapPin className="h-4 w-4" />
                {title ?? address ?? t('Unknown place')}
              </div>
              {type && <span className="text-xs text-content-secondary dark:text-content-secondary-dark">{type}</span>}
            </div>
            <div className="flex items-center gap-2 mt-1 text-[11px] text-content-secondary dark:text-content-secondary-dark flex-wrap">
              {rating != null && <span>{formatRating(rating)}</span>}
              {reviewCount != null && <span>{t('({count} reviews)', { count: reviewCount.toLocaleString() })}</span>}
              {priceLevel && <span>· {priceLevel}</span>}
              {address && title && <span>· {truncateText(address, 80)}</span>}
            </div>
          </div>
        </div>
      )}

      {/* Description */}
      {description && (
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark leading-relaxed">
          {truncateText(description, 200)}
        </p>
      )}

      {/* Service options badges */}
      {serviceOptionsList.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {serviceOptionsList.map(option => (
            <span
              key={option}
              className="inline-flex items-center rounded-full bg-surface-accent/60 dark:bg-surface-accent-dark/40 px-2 py-0.5 text-[11px] text-content-secondary dark:text-content-secondary-dark"
            >
              {option}
            </span>
          ))}
        </div>
      )}

      {/* Operating hours */}
      {(operatingHours || (operatingHoursList && operatingHoursList.length > 0)) && (
        <div className="flex items-center gap-1.5 text-xs text-content-secondary dark:text-content-secondary-dark">
          <Clock className="h-3.5 w-3.5" />
          {(operatingHours
            ? Object.entries(operatingHours)
            : Object.entries((operatingHoursList?.[0] as Record<string, unknown>) ?? {})
          )
            .slice(0, 1)
            .map(([day, hours]) => (
              <span key={day}>
                {day}: {String(hours)}
              </span>
            ))}
          {((operatingHours && Object.keys(operatingHours).length > 1) ||
            (!operatingHours && operatingHoursList && operatingHoursList.length > 1)) && (
            <span className="text-[10px]">
              {t('+{count} more', {
                count: (operatingHours ? Object.keys(operatingHours).length : operatingHoursList!.length) - 1,
              })}
            </span>
          )}
        </div>
      )}

      {/* Contact */}
      {(phone || website || (lat != null && lng != null)) && (
        <div className="flex flex-wrap gap-3 text-xs">
          {phone && <span className="text-content-secondary dark:text-content-secondary-dark">📞 {phone}</span>}
          {website && (
            <a
              href={website}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary-600 dark:text-primary-400 hover:underline truncate max-w-[200px]"
            >
              🔗 {t('Website')}
            </a>
          )}
          {lat != null && lng != null && (
            <span className="text-content-secondary dark:text-content-secondary-dark">
              📍 {lat.toFixed(4)}, {lng.toFixed(4)}
            </span>
          )}
        </div>
      )}

      {/* Multiple place results */}
      {hasPlaceResults && (
        <div className="space-y-2">
          <p className="text-[11px] font-medium text-content-secondary dark:text-content-secondary-dark">
            {t('{count} place(s) found', { count: normalizedPlaceResults!.length })}
          </p>
          {(normalizedPlaceResults as Array<Record<string, unknown>>).slice(0, 4).map((place, index) => (
            <div
              key={`place-${index}`}
              className="flex items-center gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-content dark:text-content-dark truncate">
                  {getString(place, 'title') ?? getString(place, 'name') ?? t('Unknown place')}
                </p>
                <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark truncate">
                  {getString(place, 'type') ?? getString(place, 'category') ?? ''}
                  {getString(place, 'address') ? ` · ${truncateText(getString(place, 'address')!, 60)}` : ''}
                </p>
              </div>
              {getNumber(place, 'rating') != null && (
                <span className="text-xs text-content-secondary dark:text-content-secondary-dark whitespace-nowrap">
                  {formatRating(getNumber(place, 'rating'))}
                </span>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Directions */}
      {hasDirections && (
        <div className="space-y-2">
          <p className="text-[11px] font-medium text-content-secondary dark:text-content-secondary-dark">
            <Navigation className="h-3.5 w-3.5 inline mr-1" />
            {t('{count} route(s) found', { count: directions!.length })}
          </p>
          {(directions as Array<Record<string, unknown>>).slice(0, 3).map((route, index) => {
            const numericDistance = getNumber(route, 'distance');
            const numericDuration = getNumber(route, 'duration');
            const distance =
              getString(route, 'formatted_distance') ??
              getString(route, 'distance') ??
              (numericDistance != null ? `${numericDistance} m` : '');
            const duration =
              getString(route, 'formatted_duration') ??
              getString(route, 'duration') ??
              (numericDuration != null ? `${Math.round(numericDuration / 60)} min` : '');

            return (
              <div
                key={`route-${index}`}
                className="flex items-center gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-medium text-content dark:text-content-dark truncate">
                    {getString(route, 'summary') ?? getString(route, 'via') ?? t('Route {index}', { index: index + 1 })}
                  </p>
                  <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark">
                    {distance}
                    {duration ? ` · ${duration}` : ''}
                  </p>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Review snippets */}
      {hasReviews && (
        <div className="space-y-1.5">
          <p className="text-[11px] font-medium text-content-secondary dark:text-content-secondary-dark">
            {t('Top reviews')}
          </p>
          {(reviews as Array<Record<string, unknown>>).slice(0, 2).map((review, index) => (
            <div
              key={`review-${index}`}
              className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-1.5"
            >
              <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark">
                <span className="font-medium text-content dark:text-content-dark">
                  {getString(review, 'user', 'name') ?? getString(review, 'username') ?? t('User')}
                </span>
                {getNumber(review, 'rating') != null && (
                  <span className="text-amber-600 dark:text-amber-400">
                    {formatRating(getNumber(review, 'rating'))}
                  </span>
                )}
              </div>
              {(getString(review, 'snippet') ?? getString(review, 'text')) && (
                <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark mt-0.5">
                  "{truncateText(getString(review, 'snippet') ?? getString(review, 'text') ?? '', 150)}"
                </p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
