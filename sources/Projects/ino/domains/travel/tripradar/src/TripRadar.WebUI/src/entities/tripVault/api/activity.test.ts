import { describe, expect, it } from 'vitest';
import { getTripVaultActivityState, isTripVaultActiveNow } from './activity';
import type { TripVaultItem } from './types';

const createTripVault = (
  startDate?: string | null,
  endDate?: string | null
): Pick<TripVaultItem, 'startDate' | 'endDate'> => ({
  startDate: startDate ?? null,
  endDate: endDate ?? null,
});

describe('tripVault activity', () => {
  it('returns active when no date window is set', () => {
    const trip = createTripVault(null, null);
    const now = new Date('2026-02-23T10:00:00.000Z');

    expect(getTripVaultActivityState(trip, now)).toBe('active');
    expect(isTripVaultActiveNow(trip, now)).toBe(true);
  });

  it('returns scheduled when current date is before start date', () => {
    const trip = createTripVault('2026-03-01T00:00:00.000Z', null);
    const now = new Date('2026-02-23T10:00:00.000Z');

    expect(getTripVaultActivityState(trip, now)).toBe('scheduled');
    expect(isTripVaultActiveNow(trip, now)).toBe(false);
  });

  it('returns expired when current date is after end date', () => {
    const trip = createTripVault(null, '2026-02-20T00:00:00.000Z');
    const now = new Date('2026-02-23T10:00:00.000Z');

    expect(getTripVaultActivityState(trip, now)).toBe('expired');
    expect(isTripVaultActiveNow(trip, now)).toBe(false);
  });

  it('treats day boundaries as inclusive for active state', () => {
    const trip = createTripVault('2026-02-23T00:00:00.000Z', '2026-02-23T00:00:00.000Z');
    const now = new Date('2026-02-23T23:59:59.000Z');

    expect(getTripVaultActivityState(trip, now)).toBe('active');
    expect(isTripVaultActiveNow(trip, now)).toBe(true);
  });
});
