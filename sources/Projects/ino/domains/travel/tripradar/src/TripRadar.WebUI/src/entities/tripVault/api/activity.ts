import type { TripVaultItem } from './types';

export type TripVaultActivityState = 'active' | 'scheduled' | 'expired';

const toUtcDayStart = (value: Date): number =>
  Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate());

const parseDateToUtcDayStart = (value?: string | null): number | null => {
  if (!value) {
    return null;
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  return toUtcDayStart(parsed);
};

export const getTripVaultActivityState = (
  tripVault: Pick<TripVaultItem, 'startDate' | 'endDate'>,
  now: Date = new Date()
): TripVaultActivityState => {
  const todayUtc = toUtcDayStart(now);
  const startUtcDay = parseDateToUtcDayStart(tripVault.startDate);
  const endUtcDay = parseDateToUtcDayStart(tripVault.endDate);

  if (startUtcDay !== null && todayUtc < startUtcDay) {
    return 'scheduled';
  }

  if (endUtcDay !== null && todayUtc > endUtcDay) {
    return 'expired';
  }

  return 'active';
};

export const isTripVaultActiveNow = (
  tripVault: Pick<TripVaultItem, 'startDate' | 'endDate'>,
  now: Date = new Date()
): boolean => getTripVaultActivityState(tripVault, now) === 'active';
