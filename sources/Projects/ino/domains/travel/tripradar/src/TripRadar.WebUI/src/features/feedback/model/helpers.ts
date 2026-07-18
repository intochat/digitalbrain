import type { FeedbackCategoryType } from 'entities/feedback';

export interface FeedbackCategoryOption {
  value: FeedbackCategoryType;
  label: string;
}

export const FEEDBACK_CATEGORY_LABELS: Record<FeedbackCategoryType, string> = {
  general: 'General',
  bugReport: 'Bug report',
  featureRequest: 'Feature request',
  performance: 'Performance',
  userInterface: 'User interface',
  documentation: 'Documentation',
  subscriptionCancellation: 'Subscription cancellation',
};

const NORMALIZED_TO_TYPE: Record<string, FeedbackCategoryType> = {
  general: 'general',
  bugreport: 'bugReport',
  featurerequest: 'featureRequest',
  performance: 'performance',
  userinterface: 'userInterface',
  documentation: 'documentation',
  subscriptioncancellation: 'subscriptionCancellation',
};

export const DEFAULT_CATEGORY_OPTIONS: FeedbackCategoryOption[] = (
  Object.entries(FEEDBACK_CATEGORY_LABELS) as [FeedbackCategoryType, string][]
).map(([value, label]) => ({ value, label }));

export const normalizeCategoryName = (value: string): string => value.replace(/[^a-zA-Z0-9]/g, '').toLowerCase();

export const formatCategoryName = (name: string): string =>
  name
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

export const resolveFeedbackCategoryType = (name?: string | null): FeedbackCategoryType | null => {
  if (!name) return null;
  return NORMALIZED_TO_TYPE[normalizeCategoryName(name)] ?? null;
};

export const resolveCategoryLabel = (type: FeedbackCategoryType, name?: string | null): string => {
  if (name?.trim()) return formatCategoryName(name);
  return FEEDBACK_CATEGORY_LABELS[type];
};

export const getCategoryBadgeClassName = (type: FeedbackCategoryType): string => {
  const map: Partial<Record<FeedbackCategoryType, string>> = {
    bugReport: 'bg-red-100 text-red-700 dark:bg-red-500/20 dark:text-red-300',
    featureRequest: 'bg-cyan-100 text-cyan-700 dark:bg-cyan-500/20 dark:text-cyan-300',
    performance: 'bg-orange-100 text-orange-700 dark:bg-orange-500/20 dark:text-orange-300',
    userInterface: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300',
    documentation: 'bg-violet-100 text-violet-700 dark:bg-violet-500/20 dark:text-violet-300',
    subscriptionCancellation: 'bg-slate-200 text-slate-700 dark:bg-slate-500/20 dark:text-slate-300',
  };
  return map[type] ?? 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300';
};

export const formatDate = (value?: string | null, t?: (key: string) => string): string => {
  if (!value) return t ? t('Unknown date') : 'Unknown date';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  return d.toLocaleString();
};

export const renderStars = (rating: number): string => {
  const n = Math.max(1, Math.min(5, Math.round(rating)));
  return '★'.repeat(n) + '☆'.repeat(5 - n);
};
