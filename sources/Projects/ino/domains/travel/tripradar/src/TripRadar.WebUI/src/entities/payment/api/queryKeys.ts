export const paymentKeys = {
  all: ['payment'] as const,
  subscription: () => [...paymentKeys.all, 'subscription'] as const,
  paymentMethods: () => [...paymentKeys.all, 'paymentMethods'] as const,
  invoices: () => [...paymentKeys.all, 'invoices'] as const,
  usageSummary: () => [...paymentKeys.all, 'usageSummary'] as const,
  overageUsage: () => [...paymentKeys.all, 'overageUsage'] as const,
  prices: () => [...paymentKeys.all, 'prices'] as const,
};
