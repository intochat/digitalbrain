import { describe, it, expect } from 'vitest';
import {
  PREFERENCE_CATEGORIES,
  getPreferenceCategoriesByKeys,
  getCategoryById,
  getPreferenceGroupByKey,
  getAllPreferenceKeys,
  mapServiceTypeNameToPreferenceKey,
  mapServiceTypeNamesToPreferenceKeys,
} from './categoryConfig';

describe('categoryConfig', () => {
  describe('PREFERENCE_CATEGORIES', () => {
    it('should have exactly 3 categories', () => {
      expect(PREFERENCE_CATEGORIES).toHaveLength(3);
    });

    it('should have Travel category with correct groups', () => {
      const travelCategory = PREFERENCE_CATEGORIES.find(cat => cat.id === 'travel');
      expect(travelCategory).toBeDefined();
      expect(travelCategory?.title).toBe('Travel');
      expect(travelCategory?.groups).toHaveLength(3);

      const groupKeys = travelCategory?.groups.map(group => group.key);
      expect(groupKeys).toEqual(['Flight', 'Hotel', 'Event']);
    });

    it('should have Local Services category with correct groups', () => {
      const localCategory = PREFERENCE_CATEGORIES.find(cat => cat.id === 'local-services');
      expect(localCategory).toBeDefined();
      expect(localCategory?.title).toBe('Local Services');
      expect(localCategory?.groups).toHaveLength(2);

      const groupKeys = localCategory?.groups.map(group => group.key);
      expect(groupKeys).toEqual(['Maps', 'LocalPlaces']);
    });

    it('should have General category (empty for now)', () => {
      const generalCategory = PREFERENCE_CATEGORIES.find(cat => cat.id === 'general');
      expect(generalCategory).toBeDefined();
      expect(generalCategory?.title).toBe('General');
      expect(generalCategory?.groups).toHaveLength(0);
    });

    it('should have all groups with valid components', () => {
      PREFERENCE_CATEGORIES.forEach(category => {
        category.groups.forEach(group => {
          expect(group.id).toBeTruthy();
          expect(group.title).toBeTruthy();
          expect(group.key).toBeTruthy();
          expect(group.component).toBeTruthy();
          expect(typeof group.component).toBe('function');
        });
      });
    });
  });

  describe('getCategoryById', () => {
    it('should return correct category for valid ID', () => {
      const travelCategory = getCategoryById('travel');
      expect(travelCategory?.id).toBe('travel');
      expect(travelCategory?.title).toBe('Travel');
    });

    it('should return undefined for invalid ID', () => {
      const invalidCategory = getCategoryById('invalid-id');
      expect(invalidCategory).toBeUndefined();
    });
  });

  describe('getPreferenceGroupByKey', () => {
    it('should return correct group for valid key', () => {
      const flightGroup = getPreferenceGroupByKey('Flight');
      expect(flightGroup?.key).toBe('Flight');
      expect(flightGroup?.title).toBe('Flights');
    });

    it('should return undefined for invalid key', () => {
      // @ts-expect-error - Testing invalid key
      const invalidGroup = getPreferenceGroupByKey('InvalidKey');
      expect(invalidGroup).toBeUndefined();
    });
  });

  describe('getAllPreferenceKeys', () => {
    it('should return all preference keys from all categories', () => {
      const allKeys = getAllPreferenceKeys();
      expect(allKeys).toHaveLength(5); // 3 travel + 2 local services + 0 general

      // Check that all expected keys are present
      const expectedKeys = ['Flight', 'Hotel', 'Event', 'Maps', 'LocalPlaces'];

      expectedKeys.forEach(key => {
        expect(allKeys).toContain(key);
      });
    });
  });

  describe('service type mapping', () => {
    it('maps different service name formats to preference keys', () => {
      expect(mapServiceTypeNameToPreferenceKey('flight')).toBe('Flight');
      expect(mapServiceTypeNameToPreferenceKey('Flight')).toBe('Flight');
      expect(mapServiceTypeNameToPreferenceKey('localPlaces')).toBe('LocalPlaces');
      expect(mapServiceTypeNameToPreferenceKey('local-places')).toBe('LocalPlaces');
      expect(mapServiceTypeNameToPreferenceKey('maps')).toBe('Maps');
    });

    it('returns undefined for unknown services', () => {
      expect(mapServiceTypeNameToPreferenceKey('tripAdvisorSearch')).toBeUndefined();
      expect(mapServiceTypeNameToPreferenceKey('public-transport')).toBeUndefined();
      expect(mapServiceTypeNameToPreferenceKey('points_of_interest')).toBeUndefined();
    });

    it('maps and deduplicates service names', () => {
      const keys = mapServiceTypeNamesToPreferenceKeys([
        'flight',
        'Flight',
        'local-places',
        'localPlaces',
        'unknownService',
      ]);

      expect(keys).toEqual(['Flight', 'LocalPlaces']);
    });
  });

  describe('getPreferenceCategoriesByKeys', () => {
    it('returns non-empty static categories when filter is empty', () => {
      expect(getPreferenceCategoriesByKeys()).toHaveLength(2);
      expect(getPreferenceCategoriesByKeys([])).toHaveLength(2);
      expect(getPreferenceCategoriesByKeys().map(category => category.id)).toEqual(['travel', 'local-services']);
    });

    it('returns only matching groups and removes empty sections', () => {
      const categories = getPreferenceCategoriesByKeys(['Flight', 'Maps']);

      const travelCategory = categories.find(category => category.id === 'travel');
      const localCategory = categories.find(category => category.id === 'local-services');
      const generalCategory = categories.find(category => category.id === 'general');

      expect(travelCategory?.groups.map(group => group.key)).toEqual(['Flight']);
      expect(localCategory?.groups.map(group => group.key)).toEqual(['Maps']);
      expect(generalCategory).toBeUndefined();
    });

    it('falls back to static categories when no provided keys are supported', () => {
      const categories = getPreferenceCategoriesByKeys(['FlightExplore' as never]);
      expect(categories).toHaveLength(2);
      expect(categories.map(category => category.id)).toEqual(['travel', 'local-services']);
    });
  });
});
