import type { TimezoneResponse } from 'entities/portal/api/portalApi';

export interface TimezoneOption {
  value: number;
  label: string;
}

const DEFAULT_OFFSET_LABEL = 'GMT+00:00';
const DEFAULT_LOCAL_TIME = '--:--';

const normalizeOffsetLabel = (rawOffsetLabel: string): string => {
  const cleaned = rawOffsetLabel
    .trim()
    .toUpperCase()
    .replace(/\u2212/g, '-');
  const normalizedPrefix = cleaned.startsWith('UTC') ? `GMT${cleaned.slice(3)}` : cleaned;

  if (normalizedPrefix === 'GMT') {
    return DEFAULT_OFFSET_LABEL;
  }

  const offsetMatch = /^GMT([+-])(\d{1,2})(?::?(\d{2}))?$/.exec(normalizedPrefix);
  if (!offsetMatch) {
    return DEFAULT_OFFSET_LABEL;
  }

  const sign = offsetMatch[1];
  const hours = offsetMatch[2].padStart(2, '0');
  const minutes = (offsetMatch[3] ?? '00').padStart(2, '0');
  return `GMT${sign}${hours}:${minutes}`;
};

const parseOffsetMinutes = (offsetLabel: string): number => {
  const match = /^GMT([+-])(\d{2}):(\d{2})$/.exec(offsetLabel);
  if (!match) {
    return 0;
  }

  const sign = match[1] === '+' ? 1 : -1;
  const hours = Number.parseInt(match[2], 10);
  const minutes = Number.parseInt(match[3], 10);
  return sign * (hours * 60 + minutes);
};

const formatOffsetDetails = (timezoneCode: string, now: Date): { offsetLabel: string; offsetMinutes: number } => {
  try {
    const rawOffsetLabel =
      new Intl.DateTimeFormat('en-US', {
        timeZone: timezoneCode,
        timeZoneName: 'longOffset',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      })
        .formatToParts(now)
        .find(part => part.type === 'timeZoneName')?.value ?? DEFAULT_OFFSET_LABEL;

    const offsetLabel = normalizeOffsetLabel(rawOffsetLabel);
    return { offsetLabel, offsetMinutes: parseOffsetMinutes(offsetLabel) };
  } catch {
    return { offsetLabel: DEFAULT_OFFSET_LABEL, offsetMinutes: 0 };
  }
};

const formatCurrentLocalTime = (timezoneCode: string, now: Date): string => {
  try {
    return new Intl.DateTimeFormat('en-GB', {
      timeZone: timezoneCode,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(now);
  } catch {
    return DEFAULT_LOCAL_TIME;
  }
};

export const buildTimezoneOptions = (
  timezones: TimezoneResponse[] | undefined,
  translate: (value: string) => string,
  now: Date = new Date()
): TimezoneOption[] => {
  const options = (timezones ?? []).map(timezone => {
    const baseLabel = translate(timezone.timezoneName);
    const { offsetLabel, offsetMinutes } = formatOffsetDetails(timezone.timezoneCode, now);
    const currentLocalTime = formatCurrentLocalTime(timezone.timezoneCode, now);

    return {
      value: timezone.timezoneId,
      label: `${baseLabel} (${offsetLabel}) - ${currentLocalTime}`,
      offsetMinutes,
      sortLabel: baseLabel,
    };
  });

  options.sort((left, right) => {
    if (left.offsetMinutes !== right.offsetMinutes) {
      return left.offsetMinutes - right.offsetMinutes;
    }

    return left.sortLabel.localeCompare(right.sortLabel);
  });

  return options.map(({ value, label }) => ({ value, label }));
};
