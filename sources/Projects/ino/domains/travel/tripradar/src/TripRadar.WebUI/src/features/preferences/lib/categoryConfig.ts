import type { UserPreferences } from 'shared/api';
import {
  FlightPreferenceGroup,
  HotelPreferenceGroup,
  LocalPlacesPreferenceGroup,
  MapsPreferenceGroup,
  EventPreferenceGroup,
} from '../ui';

/**
 * Configuration interface for a preference group within a category
 */
export interface PreferenceGroupConfig {
  id: string;
  title: string;
  key: keyof UserPreferences;
  component: React.ComponentType<{
    preferences: Record<string, unknown>;
    onChange: (field: string, value: unknown) => void;
    errors: Record<string, string>;
    disabled: boolean;
  }>;
}

/**
 * Configuration interface for a preference category
 */
export interface PreferenceCategory {
  id: string;
  title: string;
  description?: string;
  groups: PreferenceGroupConfig[];
}

/**
 * Main preference categories configuration.
 * Only includes groups backed by real server-side service types.
 * - Travel: Flight, Hotel, Event
 * - Local Services: Maps, LocalPlaces
 */
export const PREFERENCE_CATEGORIES: PreferenceCategory[] = [
  {
    id: 'travel',
    title: 'Travel',
    groups: [
      {
        id: 'flight',
        title: 'Flights',
        key: 'Flight',
        component: FlightPreferenceGroup,
      },
      {
        id: 'hotel',
        title: 'Hotels',
        key: 'Hotel',
        component: HotelPreferenceGroup,
      },
      {
        id: 'event',
        title: 'Events',
        key: 'Event',
        component: EventPreferenceGroup,
      },
    ],
  },
  {
    id: 'local-services',
    title: 'Local Services',
    groups: [
      {
        id: 'maps',
        title: 'Maps',
        key: 'Maps',
        component: MapsPreferenceGroup,
      },
      {
        id: 'local-places',
        title: 'Local Places',
        key: 'LocalPlaces',
        component: LocalPlacesPreferenceGroup,
      },
    ],
  },
  {
    id: 'general',
    title: 'General',
    groups: [],
  },
];

const SERVICE_NAME_TO_PREFERENCE_KEY: Record<string, keyof UserPreferences> = {
  flight: 'Flight',
  hotel: 'Hotel',
  event: 'Event',
  maps: 'Maps',
  localplaces: 'LocalPlaces',
};

const normalizeServiceTypeName = (serviceTypeName: string): string => {
  return serviceTypeName.replace(/[\s_-]/g, '').toLowerCase();
};

/**
 * Helper function to get a category by its ID
 */
export const getCategoryById = (categoryId: string): PreferenceCategory | undefined => {
  return PREFERENCE_CATEGORIES.find(category => category.id === categoryId);
};

/**
 * Helper function to get a preference group by its key
 */
export const getPreferenceGroupByKey = (key: keyof UserPreferences): PreferenceGroupConfig | undefined => {
  for (const category of PREFERENCE_CATEGORIES) {
    const group = category.groups.find(group => group.key === key);
    if (group) {
      return group;
    }
  }
  return undefined;
};

/**
 * Helper function to get all preference group keys
 */
export const getAllPreferenceKeys = (): Array<keyof UserPreferences> => {
  return PREFERENCE_CATEGORIES.flatMap(category => category.groups.map(group => group.key));
};

/**
 * Maps API service type names to preference keys used in UserPreferences.
 */
export const mapServiceTypeNameToPreferenceKey = (serviceTypeName: string): keyof UserPreferences | undefined => {
  if (!serviceTypeName) {
    return undefined;
  }

  return SERVICE_NAME_TO_PREFERENCE_KEY[normalizeServiceTypeName(serviceTypeName)];
};

/**
 * Maps and deduplicates service type names into preference keys.
 */
export const mapServiceTypeNamesToPreferenceKeys = (serviceTypeNames: string[]): Array<keyof UserPreferences> => {
  const keys: Array<keyof UserPreferences> = [];
  const seen = new Set<keyof UserPreferences>();

  for (const serviceTypeName of serviceTypeNames) {
    const key = mapServiceTypeNameToPreferenceKey(serviceTypeName);
    if (key && !seen.has(key)) {
      seen.add(key);
      keys.push(key);
    }
  }

  return keys;
};

/**
 * Filters categories by enabled preference keys while preserving category metadata.
 * Falls back to static categories when API data is missing or incompatible.
 */
export const getPreferenceCategoriesByKeys = (
  enabledPreferenceKeys?: Array<keyof UserPreferences>
): PreferenceCategory[] => {
  const nonEmptyStaticCategories = PREFERENCE_CATEGORIES.filter(category => category.groups.length > 0);

  if (!enabledPreferenceKeys || enabledPreferenceKeys.length === 0) {
    return nonEmptyStaticCategories;
  }

  const enabledKeys = new Set(enabledPreferenceKeys);

  const filteredCategories = PREFERENCE_CATEGORIES.map(category => ({
    ...category,
    groups: category.groups.filter(group => enabledKeys.has(group.key)),
  }));

  const hasAtLeastOneGroup = filteredCategories.some(category => category.groups.length > 0);
  if (!hasAtLeastOneGroup) {
    return nonEmptyStaticCategories;
  }

  return filteredCategories.filter(category => category.groups.length > 0);
};
