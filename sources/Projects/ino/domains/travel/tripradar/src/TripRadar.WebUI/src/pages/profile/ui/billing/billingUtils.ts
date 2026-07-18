/** UI-утилиты форматирования для секций биллинга (capitalize, formatDate, formatPrice, getStatusColor) */

export type StatusColorResult = { bg: string; text: string };

export const getStatusColor = (status: string): StatusColorResult => {
  switch (status) {
    case 'active':
      return {
        bg: 'bg-surface-accent dark:bg-surface-dark-tertiary',
        text: 'text-content-secondary dark:text-content-dark',
      };
    case 'trialing':
    case 'paused':
      return {
        bg: 'bg-surface-accent dark:bg-surface-dark-tertiary',
        text: 'text-content-muted dark:text-content-muted-dark',
      };
    case 'past_due':
    case 'canceled':
    case 'incomplete':
    case 'incomplete_expired':
    case 'unpaid':
      return {
        bg: 'bg-surface-accent dark:bg-surface-dark-secondary',
        text: 'text-content-muted dark:text-content-disabled-dark',
      };
    // Invoice statuses
    case 'paid':
      return {
        bg: 'bg-green-100 dark:bg-green-500/20',
        text: 'text-green-800 dark:text-green-400',
      };
    case 'open':
      return {
        bg: 'bg-yellow-100 dark:bg-yellow-500/20',
        text: 'text-yellow-800 dark:text-yellow-400',
      };
    case 'void':
    case 'uncollectible':
      return {
        bg: 'bg-red-100 dark:bg-red-500/20',
        text: 'text-red-800 dark:text-red-400',
      };
    case 'draft':
      return {
        bg: 'bg-gray-100 dark:bg-gray-500/20',
        text: 'text-gray-800 dark:text-gray-400',
      };
    default:
      return {
        bg: 'bg-surface-accent dark:bg-surface-dark-tertiary',
        text: 'text-content-muted dark:text-content-muted-dark',
      };
  }
};

export const formatDate = (dateStr: string, locale?: string): string => {
  try {
    return new Intl.DateTimeFormat(locale, { year: 'numeric', month: 'long', day: 'numeric' }).format(
      new Date(dateStr)
    );
  } catch {
    return new Date(dateStr).toLocaleDateString();
  }
};

export const formatPrice = (amount?: number, currency?: string | null, locale?: string) => {
  if (amount == null) return '';
  const currencyCode = currency?.toUpperCase() || 'USD';
  try {
    return new Intl.NumberFormat(locale, { style: 'currency', currency: currencyCode }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currencyCode}`;
  }
};

export const capitalize = (str: string) => str.charAt(0).toUpperCase() + str.slice(1);
