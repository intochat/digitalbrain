import type { UsageSourceType } from 'entities/usage/api';

export const RANGE_OPTIONS = [7, 30, 90] as const;
export type RangeOption = (typeof RANGE_OPTIONS)[number];

export const SOURCE_ORDER: UsageSourceType[] = ['api', 'scheduled', 'telegram', 'ai'];

export const SOURCE_META: Record<UsageSourceType, { labelKey: string; segmentClass: string; dotClass: string }> = {
  api: {
    labelKey: 'API',
    segmentClass: 'bg-red-500 dark:bg-red-400',
    dotClass: 'bg-red-500 dark:bg-red-400',
  },
  scheduled: {
    labelKey: 'Scheduled',
    segmentClass: 'bg-amber-500 dark:bg-amber-400',
    dotClass: 'bg-amber-500 dark:bg-amber-400',
  },
  telegram: {
    labelKey: 'Telegram',
    segmentClass: 'bg-sky-500 dark:bg-sky-400',
    dotClass: 'bg-sky-500 dark:bg-sky-400',
  },
  ai: {
    labelKey: 'AI',
    segmentClass: 'bg-violet-500 dark:bg-violet-400',
    dotClass: 'bg-violet-500 dark:bg-violet-400',
  },
};
