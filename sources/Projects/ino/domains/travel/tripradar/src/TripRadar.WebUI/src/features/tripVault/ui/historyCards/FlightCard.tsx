import { Plane } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  formatDuration,
  formatPrice,
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface FlightCardProps {
  item: TripHistoryItem;
}

interface ParsedFlight {
  airline: string | null;
  airlineLogo: string | null;
  price: number | null;
  totalDuration: number | null;
  segmentCount: number;
  departureAirport: string | null;
  arrivalAirport: string | null;
  type: string | null;
}

const parseBestFlights = (data: Record<string, unknown>): ParsedFlight[] => {
  const bestFlights = getArray(data, 'bestFlights') ?? getArray(data, 'best_flights') ?? [];
  const otherFlights = getArray(data, 'otherFlights') ?? getArray(data, 'other_flights') ?? [];
  const allFlights = [...bestFlights, ...otherFlights].slice(0, 4);

  return allFlights.map(flight => {
    const f = flight as Record<string, unknown>;
    const segments = getArray(f, 'flights') ?? [];
    const firstSegment = segments.length > 0 ? (segments[0] as Record<string, unknown>) : null;
    const lastSegment = segments.length > 0 ? (segments[segments.length - 1] as Record<string, unknown>) : null;

    return {
      airline: firstSegment ? (getString(firstSegment, 'airline') ?? null) : null,
      airlineLogo: firstSegment
        ? (getString(firstSegment, 'airlineLogo') ?? getString(firstSegment, 'airline_logo') ?? null)
        : null,
      price: getNumber(f, 'price'),
      totalDuration: getNumber(f, 'totalDuration') ?? getNumber(f, 'total_duration'),
      segmentCount: segments.length,
      departureAirport: firstSegment
        ? (getString(firstSegment, 'departureAirport', 'name') ??
          getString(firstSegment, 'departure_airport', 'name') ??
          null)
        : null,
      arrivalAirport: lastSegment
        ? (getString(lastSegment, 'arrivalAirport', 'name') ??
          getString(lastSegment, 'arrival_airport', 'name') ??
          null)
        : null,
      type: getString(f, 'type'),
    };
  });
};

export const FlightCard = ({ item }: FlightCardProps) => {
  const data = safeParse(item.resultSummary);
  const queryData = safeParse(item.queryParametersJson);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const flights = parseBestFlights(data).filter(f => f.airline != null || f.price != null);
  const priceInsights = getObject(data, 'priceInsights') ?? getObject(data, 'price_insights');
  const searchParams = getObject(data, 'searchParameters') ?? getObject(data, 'search_parameters');

  const departure = searchParams
    ? (getString(searchParams, 'departureId') ?? getString(searchParams, 'departure_id') ?? null)
    : null;
  const arrival = searchParams
    ? (getString(searchParams, 'arrivalId') ?? getString(searchParams, 'arrival_id') ?? null)
    : null;
  const outboundDate = searchParams
    ? (getString(searchParams, 'outboundDate') ?? getString(searchParams, 'outbound_date') ?? null)
    : null;

  // Try query params for route if not in result
  const queryDeparture = queryData
    ? (getString(queryData, 'flightSearch', 'departureAirportCode') ??
      getString(queryData, 'flightSearch', 'departure_airport_code') ??
      departure)
    : departure;
  const queryArrival = queryData
    ? (getString(queryData, 'flightSearch', 'arrivalAirportCode') ??
      getString(queryData, 'flightSearch', 'arrival_airport_code') ??
      arrival)
    : arrival;

  const lowestPrice = priceInsights
    ? (getNumber(priceInsights, 'lowestPrice') ?? getNumber(priceInsights, 'lowest_price'))
    : null;
  const priceLevel = priceInsights
    ? (getString(priceInsights, 'priceLevel') ?? getString(priceInsights, 'price_level'))
    : null;

  return (
    <div className="space-y-3">
      {/* Route header */}
      <div className="flex items-center gap-3 flex-wrap">
        {(queryDeparture || queryArrival) && (
          <div className="inline-flex items-center gap-2 rounded-lg bg-sky-50 dark:bg-sky-500/10 px-3 py-1.5 text-sm font-semibold text-sky-700 dark:text-sky-300">
            <Plane className="h-4 w-4" />
            {queryDeparture ?? '?'} → {queryArrival ?? '?'}
          </div>
        )}
        {outboundDate && (
          <span className="text-xs text-content-secondary dark:text-content-secondary-dark">{outboundDate}</span>
        )}
      </div>

      {/* Price insights */}
      {priceInsights && (
        <div className="flex flex-wrap gap-2">
          {lowestPrice != null && (
            <span className="inline-flex items-center rounded-full bg-emerald-50 dark:bg-emerald-500/10 px-2.5 py-1 text-xs font-medium text-emerald-700 dark:text-emerald-300">
              Lowest: {formatPrice(lowestPrice)}
            </span>
          )}
          {priceLevel && (
            <span className="inline-flex items-center rounded-full bg-amber-50 dark:bg-amber-500/10 px-2.5 py-1 text-xs font-medium text-amber-700 dark:text-amber-300">
              Price level: {priceLevel}
            </span>
          )}
        </div>
      )}

      {/* Flight options list */}
      {flights.length > 0 && (
        <div className="space-y-2">
          {flights.map((flight, index) => (
            <div
              key={`flight-${index}`}
              className="flex items-center gap-3 rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark px-3 py-2"
            >
              <ResultImage
                src={flight.airlineLogo}
                alt={flight.airline ?? 'Airline'}
                variant="flight"
                className="h-6 w-6"
              />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-medium text-content dark:text-content-dark truncate">
                  {flight.airline ?? 'Unknown airline'}
                  {flight.departureAirport && flight.arrivalAirport && (
                    <span className="text-content-secondary dark:text-content-secondary-dark font-normal">
                      {' · '}
                      {flight.departureAirport} → {flight.arrivalAirport}
                    </span>
                  )}
                </p>
                <div className="flex items-center gap-2 text-[11px] text-content-secondary dark:text-content-secondary-dark">
                  <span>{formatDuration(flight.totalDuration)}</span>
                  <span>·</span>
                  <span>
                    {flight.segmentCount === 1
                      ? 'Direct'
                      : `${flight.segmentCount - 1} stop${flight.segmentCount > 2 ? 's' : ''}`}
                  </span>
                  {flight.type && (
                    <>
                      <span>·</span>
                      <span className="capitalize">{flight.type}</span>
                    </>
                  )}
                </div>
              </div>
              {flight.price != null && (
                <span className="text-sm font-semibold text-content dark:text-content-dark whitespace-nowrap">
                  {formatPrice(flight.price)}
                </span>
              )}
            </div>
          ))}
        </div>
      )}

      {flights.length === 0 && (
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
          No flight options available in the response snapshot.
        </p>
      )}
    </div>
  );
};
