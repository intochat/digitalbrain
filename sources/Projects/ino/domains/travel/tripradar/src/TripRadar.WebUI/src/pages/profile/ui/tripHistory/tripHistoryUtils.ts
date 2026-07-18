import { frontendI18n } from 'app/i18n';
import type { ServiceType } from 'entities/tripVault';

export const HISTORY_PAGE_SIZES = [10, 25, 50];
export const ACTIVE_TRIP_VAULT_STORAGE_KEY = 'tripradar.activeTripVaultUniqueId';

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

export const formatDateTime = (value?: string | null): string => {
  if (!value) return frontendI18n.t('Not set');
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString();
};

export const resolveServiceType = (serviceType: ServiceType): string => {
  const raw = serviceType as unknown;
  if (typeof raw === 'number') return SERVICE_TYPE_MAP[raw] ?? String(raw);
  return String(raw);
};

export const humanizeServiceType = (serviceType: ServiceType): string =>
  resolveServiceType(serviceType).replace(/([a-z])([A-Z])/g, '$1 $2');

const BADGE_COLORS: Array<{ match: string; cls: string }> = [
  {
    match: 'flight',
    cls: 'border border-sky-200/80 bg-sky-50 text-sky-700 dark:border-sky-500/30 dark:bg-sky-500/15 dark:text-sky-300',
  },
  {
    match: 'hotel',
    cls: 'border border-emerald-200/80 bg-emerald-50 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/15 dark:text-emerald-300',
  },
  {
    match: 'event',
    cls: 'border border-violet-200/80 bg-violet-50 text-violet-700 dark:border-violet-500/30 dark:bg-violet-500/15 dark:text-violet-300',
  },
  {
    match: 'map',
    cls: 'border border-amber-200/80 bg-amber-50 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-300',
  },
  {
    match: 'local',
    cls: 'border border-teal-200/80 bg-teal-50 text-teal-700 dark:border-teal-500/30 dark:bg-teal-500/15 dark:text-teal-300',
  },
  {
    match: 'place',
    cls: 'border border-teal-200/80 bg-teal-50 text-teal-700 dark:border-teal-500/30 dark:bg-teal-500/15 dark:text-teal-300',
  },
  {
    match: 'yelp',
    cls: 'border border-rose-200/80 bg-rose-50 text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/15 dark:text-rose-300',
  },
  {
    match: 'tripadvisor',
    cls: 'border border-rose-200/80 bg-rose-50 text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/15 dark:text-rose-300',
  },
  {
    match: 'opentable',
    cls: 'border border-orange-200/80 bg-orange-50 text-orange-700 dark:border-orange-500/30 dark:bg-orange-500/15 dark:text-orange-300',
  },
  {
    match: 'google',
    cls: 'border border-slate-200/80 bg-slate-100 text-slate-700 dark:border-slate-500/30 dark:bg-slate-500/15 dark:text-slate-300',
  },
];

const DEFAULT_BADGE =
  'border border-slate-200/80 bg-slate-100 text-slate-700 dark:border-slate-500/30 dark:bg-slate-500/15 dark:text-slate-300';

export const toServiceBadgeClass = (serviceType: ServiceType): string => {
  const normalized = resolveServiceType(serviceType).toLowerCase();
  return BADGE_COLORS.find(b => normalized.includes(b.match))?.cls ?? DEFAULT_BADGE;
};

// --- History filter tabs ---

export type HistoryFilterTab = 'all' | 'flights' | 'hotels' | 'events' | 'local-places' | 'maps' | 'other';

export interface HistoryFilterTabConfig {
  value: HistoryFilterTab;
  labelKey: string;
  match: readonly string[];
}

const HISTORY_FILTER_TABS: HistoryFilterTabConfig[] = [
  { value: 'all', labelKey: 'All', match: [] },
  { value: 'flights', labelKey: 'Flights', match: ['flight', 'flightexplore'] },
  { value: 'hotels', labelKey: 'Hotels', match: ['hotel'] },
  { value: 'events', labelKey: 'Events', match: ['event'] },
  { value: 'local-places', labelKey: 'Local Places', match: ['localplaces'] },
  { value: 'maps', labelKey: 'Maps', match: ['maps', 'mapsdirections', 'mapsplaceresults', 'placereview'] },
  {
    value: 'other',
    labelKey: 'Other',
    match: [
      'tripadvisorsearch',
      'tripadvisorplace',
      'opentablereview',
      'googlevideosearch',
      'yelpsearch',
      'yelpplace',
      'yelpreviews',
      'yelpplacefullmenu',
      'googlelightsearch',
    ],
  },
];

const resolveServiceTypeLower = (serviceType: ServiceType): string => resolveServiceType(serviceType).toLowerCase();

const matchesTab = (serviceType: ServiceType, tab: HistoryFilterTabConfig): boolean => {
  if (tab.value === 'all') return true;
  const resolved = resolveServiceTypeLower(serviceType);
  return tab.match.some(m => m === resolved);
};

export const getHistoryFilterTabs = (): HistoryFilterTabConfig[] => HISTORY_FILTER_TABS;

export const computeHistoryFilterCounts = (
  items: Array<{ serviceType: ServiceType }>
): Record<HistoryFilterTab, number> => {
  const counts = Object.fromEntries(HISTORY_FILTER_TABS.map(tab => [tab.value, 0])) as Record<HistoryFilterTab, number>;
  counts.all = items.length;

  for (const item of items) {
    for (const tab of HISTORY_FILTER_TABS) {
      if (tab.value !== 'all' && matchesTab(item.serviceType, tab)) {
        counts[tab.value]++;
      }
    }
  }

  return counts;
};

export const filterHistoryItems = <T extends { serviceType: ServiceType }>(
  items: T[],
  activeTab: HistoryFilterTab
): T[] => {
  if (activeTab === 'all') return items;
  const tab = HISTORY_FILTER_TABS.find(t => t.value === activeTab);
  if (!tab) return items;
  return items.filter(item => matchesTab(item.serviceType, tab));
};
