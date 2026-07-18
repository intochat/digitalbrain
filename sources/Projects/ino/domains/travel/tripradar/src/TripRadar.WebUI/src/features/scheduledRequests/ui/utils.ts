import { frontendI18n } from 'app/i18n';
import type {
  AirportSuggestionItem,
  CreateScheduledRequestPayload,
  ScheduledExecutionItem,
  ScheduledQueryType,
  UpdateScheduledExecutionQueryRequest,
} from 'entities/scheduledRequests';
import type { ScheduledRequestFormState, ScheduleOption, ServiceBadgeConfig } from './constants';

// --- New types for redesign ---

export interface SummaryData {
  activeCount: number;
  pausedCount: number;
  nextExecutionTime: string | null;
}

export type FilterTabValue = ScheduledQueryType | 'all';

export type FilterTabCounts = Record<FilterTabValue, number>;

export const SCHEDULE_OPTIONS: ScheduleOption[] = [
  { value: '0 * * * *', label: 'Every hour' },
  { value: '0 */3 * * *', label: 'Every 3 hours' },
  { value: '0 */6 * * *', label: 'Every 6 hours' },
  { value: '0 */12 * * *', label: 'Every 12 hours' },
  { value: '0 0 * * *', label: 'Daily' },
  { value: '0 0 * * 0', label: 'Weekly' },
];

export const FIRST_TRIP_REQUEST_EVENT_STORAGE_KEY = 'tripradar.telemetry.firstTripRequest.v1';

export const normalizeAirportCode = (value: string): string => value.trim().toUpperCase();

export const isIataCode = (value: string): boolean => /^[A-Z]{3}$/.test(value.trim());

export const isManualIataCodeInput = (value: string): boolean => isIataCode(value);

const WORD_START_REGEX = /(^|[\s\-/(])([A-Za-z\u0400-\u04FF])/g;
const CYRILLIC_REGEX = /[\u0400-\u04FF]/i;
const AIRPORT_ALIAS_REGEX = /аэропорт/i;

export const toDisplayCityName = (value?: string | null): string | null => {
  if (!value) return null;
  const trimmed = value.trim();
  if (!trimmed) return null;

  return trimmed.replace(
    WORD_START_REGEX,
    (_, prefix: string, letter: string) => `${prefix}${letter.toLocaleUpperCase()}`
  );
};

const getAirportAliases = (airport: AirportSuggestionItem): string[] =>
  (airport.searchAliases ?? '')
    .split('|')
    .map(alias => alias.trim())
    .filter(alias => alias.length > 0);

const sortAliases = (aliases: string[]): string[] => [...aliases].sort((left, right) => left.length - right.length);

const findMatchingAliases = (aliases: string[], query: string): string[] => {
  const normalizedQuery = query.trim().toLocaleLowerCase();
  if (!normalizedQuery) return aliases;
  const matchingAliases = aliases.filter(alias => alias.toLocaleLowerCase().includes(normalizedQuery));
  return matchingAliases.length > 0 ? matchingAliases : aliases;
};

const chooseLocalizedAlias = (
  aliases: string[],
  query: string,
  predicate?: (alias: string) => boolean
): string | null => {
  const matchingAliases = findMatchingAliases(aliases, query);
  const preferredAliases = predicate ? matchingAliases.filter(predicate) : matchingAliases;
  if (preferredAliases.length > 0) return sortAliases(preferredAliases)[0];

  if (!predicate) return matchingAliases[0] ?? null;

  const fallbackAliases = aliases.filter(predicate);
  return fallbackAliases.length > 0 ? sortAliases(fallbackAliases)[0] : null;
};

const getLocalizedAirportAliases = (
  airport: AirportSuggestionItem,
  query: string,
  language: string
): { locationLabel: string; airportLabel: string | null } | null => {
  if (!language.startsWith('ru')) {
    return null;
  }

  const cyrillicAliases = getAirportAliases(airport).filter(alias => CYRILLIC_REGEX.test(alias));
  if (cyrillicAliases.length === 0) {
    return null;
  }

  const locationAlias =
    chooseLocalizedAlias(cyrillicAliases, query, alias => !AIRPORT_ALIAS_REGEX.test(alias)) ??
    chooseLocalizedAlias(cyrillicAliases, query);

  const airportAlias = chooseLocalizedAlias(cyrillicAliases, query, alias => AIRPORT_ALIAS_REGEX.test(alias));

  if (!locationAlias) {
    return null;
  }

  return {
    locationLabel: toDisplayCityName(locationAlias) || locationAlias,
    airportLabel: airportAlias ? toDisplayCityName(airportAlias) || airportAlias : null,
  };
};

export const getAirportSuggestionDisplay = (
  airport: AirportSuggestionItem,
  query: string,
  language: string
): { locationLabel: string; airportLabel: string } => {
  const cityLabel = toDisplayCityName(airport.city) || airport.city;
  const defaultLocationLabel = airport.countryCode ? `${cityLabel}, ${airport.countryCode}` : cityLabel;
  const defaultAirportLabel = toDisplayCityName(airport.name) || airport.name;
  const localizedAliases = getLocalizedAirportAliases(airport, query, language);

  if (!localizedAliases) {
    return { locationLabel: defaultLocationLabel, airportLabel: defaultAirportLabel };
  }

  return {
    locationLabel: localizedAliases.locationLabel,
    airportLabel: localizedAliases.airportLabel ?? defaultAirportLabel,
  };
};

export const formatAirportSuggestion = (
  airport: AirportSuggestionItem,
  query = '',
  language = frontendI18n.resolvedLanguage ?? frontendI18n.language
): string => {
  const display = getAirportSuggestionDisplay(airport, query, language);
  return `${display.locationLabel} - ${display.airportLabel} (${airport.code})`;
};

const getRussianCityAlias = (searchAliases?: string | null): string | null => {
  if (!searchAliases) return null;
  const aliases = searchAliases
    .split('|')
    .map(a => a.trim())
    .filter(a => a.length > 0);
  const cityAliases = aliases.filter(alias => CYRILLIC_REGEX.test(alias) && !AIRPORT_ALIAS_REGEX.test(alias));
  if (cityAliases.length > 0) return sortAliases(cityAliases)[0];
  const cyrillicAliases = aliases.filter(alias => CYRILLIC_REGEX.test(alias));
  return cyrillicAliases.length > 0 ? sortAliases(cyrillicAliases)[0] : null;
};

export const formatAirportDisplay = (
  city?: string | null,
  code?: string | null,
  searchAliases?: string | null
): string => {
  const lang = frontendI18n.resolvedLanguage ?? frontendI18n.language;
  const isRussian = lang.startsWith('ru');
  const cityLabel = isRussian
    ? (toDisplayCityName(getRussianCityAlias(searchAliases)) ?? toDisplayCityName(city))
    : toDisplayCityName(city);
  const normalizedCode = code?.trim().toUpperCase();
  if (cityLabel && normalizedCode) return `${cityLabel} (${normalizedCode})`;
  return cityLabel || normalizedCode || frontendI18n.t('Unknown');
};

export const getExecutionRouteLabel = (execution: ScheduledExecutionItem, t: (key: string) => string): string =>
  `${formatAirportDisplay(execution.departureAirportCity, execution.departureAirportCode, execution.departureAirportSearchAliases)} ${t('to')} ${formatAirportDisplay(execution.destinationAirportCity, execution.destinationAirportCode, execution.destinationAirportSearchAliases)}`;

export const getExecutionCardTitle = (execution: ScheduledExecutionItem): string => {
  if (execution.departureAirportCode && execution.destinationAirportCode) {
    return `${formatAirportDisplay(execution.departureAirportCity, execution.departureAirportCode, execution.departureAirportSearchAliases)} -> ${formatAirportDisplay(execution.destinationAirportCity, execution.destinationAirportCode, execution.destinationAirportSearchAliases)}`;
  }
  return execution.requestSummary;
};

const SCHEDULED_SERVICE_TYPE_MAP: Record<number, string> = {
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

export const resolveScheduledServiceType = (serviceType: string): string => {
  const raw = serviceType as unknown;
  if (typeof raw === 'number') return SCHEDULED_SERVICE_TYPE_MAP[raw] ?? String(raw);
  return String(raw);
};

export const resolveQueryTypeFromServiceType = (serviceType: string): ScheduledQueryType => {
  const normalized = resolveScheduledServiceType(serviceType).toLowerCase();
  if (normalized.includes('flight')) return 'flights';
  if (normalized.includes('hotel')) return 'hotels';
  if (normalized.includes('event')) return 'events';
  return 'local-places';
};

export const resolveServiceBadge = (serviceType: string): ServiceBadgeConfig => {
  const resolved = resolveScheduledServiceType(serviceType);
  const normalized = resolved.toLowerCase();
  if (normalized.includes('flight'))
    return { label: 'Flights', className: 'bg-sky-100 text-sky-700 dark:bg-sky-500/20 dark:text-sky-300' };
  if (normalized.includes('hotel'))
    return {
      label: 'Hotels',
      className: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300',
    };
  if (normalized.includes('event'))
    return { label: 'Events', className: 'bg-violet-100 text-violet-700 dark:bg-violet-500/20 dark:text-violet-300' };
  if (normalized.includes('local'))
    return {
      label: 'Local Places',
      className: 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300',
    };
  const humanized = resolved.replace(/([a-z])([A-Z])/g, '$1 $2');
  return { label: humanized, className: 'bg-slate-200 text-slate-700 dark:bg-slate-500/20 dark:text-slate-300' };
};

export const resolveLocale = (language: string): string => (language.startsWith('ru') ? 'ru-RU' : 'en-US');

export const formatExecutionDateTime = (
  value: string | null | undefined,
  locale: string,
  notSetLabel: string
): string => {
  if (!value) return notSetLabel;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString(locale, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
};

export const formatExecutionDate = (value: string | null | undefined, locale: string, notSetLabel: string): string => {
  if (!value) return notSetLabel;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleDateString(locale, { year: 'numeric', month: 'numeric', day: 'numeric' });
};

export const getDefaultNextExecutionTime = (): string => {
  const now = new Date();
  now.setHours(now.getHours() + 1, 0, 0, 0);
  const timezoneOffsetInMs = now.getTimezoneOffset() * 60 * 1000;
  return new Date(now.getTime() - timezoneOffsetInMs).toISOString().slice(0, 16);
};

export const toDateInputValue = (value?: string | null): string => {
  if (!value) return '';
  const isoDateMatch = value.match(/^\d{4}-\d{2}-\d{2}/);
  if (isoDateMatch) return isoDateMatch[0];
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';
  const timezoneOffsetInMs = parsed.getTimezoneOffset() * 60 * 1000;
  return new Date(parsed.getTime() - timezoneOffsetInMs).toISOString().slice(0, 10);
};

export const toDateTimeLocalInputValue = (value?: string | null): string => {
  if (!value) return getDefaultNextExecutionTime();
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return getDefaultNextExecutionTime();
  const timezoneOffsetInMs = parsed.getTimezoneOffset() * 60 * 1000;
  return new Date(parsed.getTime() - timezoneOffsetInMs).toISOString().slice(0, 16);
};

export const toTimeInputValue = (value?: string | null): string => {
  return toDateTimeLocalInputValue(value).slice(11, 16);
};

export const combineDateAndTimeInputValue = (date: string, time: string): string => {
  if (!date) return '';
  return `${date}T${time || '00:00'}`;
};

export const createInitialFormState = (): ScheduledRequestFormState => ({
  queryType: 'flights',
  schedule: SCHEDULE_OPTIONS[0].value,
  nextExecutionTime: getDefaultNextExecutionTime(),
  searchQuery: '',
  location: '',
  radius: '',
  departureAirportCode: '',
  destinationAirportCode: '',
  departureDate: '',
  returnDate: '',
  checkInDate: '',
  checkOutDate: '',
  startDate: '',
  endDate: '',
});

export const buildCreatePayload = (formState: ScheduledRequestFormState): CreateScheduledRequestPayload => {
  const nextExecutionTime = new Date(formState.nextExecutionTime).toISOString();
  const schedule = formState.schedule;

  if (formState.queryType === 'events') {
    return {
      queryType: 'events',
      payload: {
        searchQuery: formState.searchQuery.trim(),
        location: formState.location.trim(),
        startDate: formState.startDate || undefined,
        endDate: formState.endDate || undefined,
        schedule,
        nextExecutionTime,
      },
    };
  }

  if (formState.queryType === 'hotels') {
    return {
      queryType: 'hotels',
      payload: {
        location: formState.location.trim(),
        checkInDate: formState.checkInDate,
        checkOutDate: formState.checkOutDate,
        schedule,
        nextExecutionTime,
      },
    };
  }

  if (formState.queryType === 'local-places') {
    return {
      queryType: 'local-places',
      payload: {
        searchQuery: formState.searchQuery.trim(),
        location: formState.location.trim(),
        radius: formState.radius.trim() ? Number(formState.radius) : undefined,
        schedule,
        nextExecutionTime,
      },
    };
  }

  return {
    queryType: 'flights',
    payload: {
      departureAirportCode: formState.departureAirportCode.trim().toUpperCase(),
      destinationAirportCode: formState.destinationAirportCode.trim().toUpperCase(),
      departureDate: formState.departureDate,
      returnDate: formState.returnDate || undefined,
      schedule,
      nextExecutionTime,
    },
  };
};

export const buildUpdateQueryPayload = (formState: ScheduledRequestFormState): UpdateScheduledExecutionQueryRequest => {
  if (formState.queryType === 'events') {
    return { searchQuery: formState.searchQuery.trim(), location: formState.location.trim() };
  }
  if (formState.queryType === 'hotels') {
    return {
      location: formState.location.trim(),
      checkInDate: formState.checkInDate || undefined,
      checkOutDate: formState.checkOutDate || undefined,
    };
  }
  if (formState.queryType === 'local-places') {
    return {
      searchQuery: formState.searchQuery.trim(),
      location: formState.location.trim(),
      radius: formState.radius.trim() ? Number(formState.radius) : undefined,
    };
  }
  return {
    departureAirportCode: formState.departureAirportCode.trim().toUpperCase(),
    destinationAirportCode: formState.destinationAirportCode.trim().toUpperCase(),
    departureDate: formState.departureDate || undefined,
    returnDate: formState.returnDate || undefined,
  };
};

export const getValidationError = (
  formState: ScheduledRequestFormState,
  requireFutureNextExecutionTime: boolean = true
): string | null => {
  if (!formState.nextExecutionTime) return frontendI18n.t('Next execution time is required.');
  const nextExecutionDate = new Date(formState.nextExecutionTime);
  if (Number.isNaN(nextExecutionDate.getTime())) return frontendI18n.t('Next execution time is invalid.');
  if (requireFutureNextExecutionTime && nextExecutionDate.getTime() <= Date.now())
    return frontendI18n.t('Next execution time must be in the future.');

  if (formState.queryType === 'events') {
    if (!formState.searchQuery.trim()) return frontendI18n.t('Search query is required for event requests.');
    if (!formState.location.trim()) return frontendI18n.t('Location is required for event requests.');
    if (formState.startDate && formState.endDate && formState.endDate < formState.startDate)
      return frontendI18n.t('Event end date must be on or after the start date.');
  }

  if (formState.queryType === 'flights') {
    if (formState.departureAirportCode.trim().length !== 3 || formState.destinationAirportCode.trim().length !== 3)
      return frontendI18n.t('Flight airport codes must be exactly 3 letters.');
    if (formState.departureAirportCode.trim().toUpperCase() === formState.destinationAirportCode.trim().toUpperCase())
      return frontendI18n.t('Origin and destination airport codes cannot match.');
    if (!formState.departureDate) return frontendI18n.t('Departure date is required for flight requests.');
    const now = new Date();
    const timezoneOffsetInMs = now.getTimezoneOffset() * 60 * 1000;
    const localCurrentDate = new Date(now.getTime() - timezoneOffsetInMs).toISOString().slice(0, 10);
    if (formState.departureDate < localCurrentDate) return frontendI18n.t('Departure date must be today or later.');
    if (formState.returnDate && formState.returnDate < formState.departureDate)
      return frontendI18n.t('Return date must be after departure date.');
  }

  if (formState.queryType === 'hotels') {
    if (!formState.location.trim()) return frontendI18n.t('Location is required for hotel requests.');
    if (!formState.checkInDate || !formState.checkOutDate)
      return frontendI18n.t('Check-in and check-out dates are required for hotel requests.');
    if (formState.checkOutDate <= formState.checkInDate)
      return frontendI18n.t('Check-out date must be after check-in date.');
  }

  if (formState.queryType === 'local-places') {
    if (!formState.searchQuery.trim()) return frontendI18n.t('Search query is required for local places requests.');
    if (!formState.location.trim()) return frontendI18n.t('Location is required for local places requests.');
    if (formState.radius.trim() && Number(formState.radius) <= 0)
      return frontendI18n.t('Radius must be greater than zero.');
  }

  return null;
};

// --- New utility functions for redesign ---

export const deriveSummary = (executions: ScheduledExecutionItem[]): SummaryData => {
  let activeCount = 0;
  let pausedCount = 0;
  let earliest: string | null = null;

  for (const item of executions) {
    if (item.isActive) activeCount++;
    else pausedCount++;

    if (item.nextExecutionTime && (!earliest || item.nextExecutionTime < earliest)) {
      earliest = item.nextExecutionTime;
    }
  }

  return { activeCount, pausedCount, nextExecutionTime: earliest };
};

export const filterByType = (executions: ScheduledExecutionItem[], filter: FilterTabValue): ScheduledExecutionItem[] =>
  filter === 'all' ? executions : executions.filter(e => resolveQueryTypeFromServiceType(e.serviceType) === filter);

export const computeFilterCounts = (executions: ScheduledExecutionItem[]): FilterTabCounts => {
  const counts: FilterTabCounts = { all: executions.length, flights: 0, hotels: 0, events: 0, 'local-places': 0 };
  for (const item of executions) {
    const type = resolveQueryTypeFromServiceType(item.serviceType);
    counts[type]++;
  }
  return counts;
};

export const sortByNextExecution = (executions: ScheduledExecutionItem[]): ScheduledExecutionItem[] =>
  [...executions].sort((a, b) => a.nextExecutionTime.localeCompare(b.nextExecutionTime));
