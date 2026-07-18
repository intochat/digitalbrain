import { describe, expect, it } from 'vitest';
import { buildTimezoneOptions } from './timezoneOptions';

describe('buildTimezoneOptions', () => {
  it('formats each option with GMT offset and current timezone time', () => {
    const now = new Date('2026-01-15T12:00:00.000Z');

    const options = buildTimezoneOptions(
      [
        { timezoneId: 1, timezoneCode: 'UTC', timezoneName: 'UTC' },
        { timezoneId: 2, timezoneCode: 'Asia/Kolkata', timezoneName: 'India Standard Time' },
      ],
      value => value,
      now
    );

    expect(options).toEqual([
      { value: 1, label: 'UTC (GMT+00:00) - 12:00' },
      { value: 2, label: 'India Standard Time (GMT+05:30) - 17:30' },
    ]);
  });

  it('sorts by GMT offset and keeps translated base labels', () => {
    const now = new Date('2026-01-15T12:00:00.000Z');

    const options = buildTimezoneOptions(
      [
        { timezoneId: 10, timezoneCode: 'Europe/Berlin', timezoneName: 'Berlin (CET)' },
        { timezoneId: 9, timezoneCode: 'America/New_York', timezoneName: 'Eastern Time (ET)' },
      ],
      value => {
        if (value === 'Berlin (CET)') {
          return 'Translated Berlin (CET)';
        }

        if (value === 'Eastern Time (ET)') {
          return 'Translated Eastern Time (ET)';
        }

        return value;
      },
      now
    );

    expect(options).toEqual([
      { value: 9, label: 'Translated Eastern Time (ET) (GMT-05:00) - 07:00' },
      { value: 10, label: 'Translated Berlin (CET) (GMT+01:00) - 13:00' },
    ]);
  });

  it('falls back to UTC offset when timezone code is invalid', () => {
    const now = new Date('2026-01-15T12:00:00.000Z');

    const options = buildTimezoneOptions(
      [{ timezoneId: 99, timezoneCode: 'Invalid/Timezone', timezoneName: 'Custom Zone' }],
      value => value,
      now
    );

    expect(options).toEqual([{ value: 99, label: 'Custom Zone (GMT+00:00) - --:--' }]);
  });
});
