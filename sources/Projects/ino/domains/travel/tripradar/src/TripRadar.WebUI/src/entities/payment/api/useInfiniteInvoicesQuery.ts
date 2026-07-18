import { useInfiniteQuery } from '@tanstack/react-query';
import { paymentApi } from './paymentApi';
import { paymentKeys } from './queryKeys';

const INVOICES_LIMIT = 20;

export const useInfiniteInvoicesQuery = () => {
  return useInfiniteQuery({
    queryKey: paymentKeys.invoices(),
    queryFn: ({ pageParam }) => paymentApi.getInvoices({ limit: INVOICES_LIMIT, startingAfter: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: lastPage => {
      if (!lastPage.hasMore) return undefined;
      return lastPage.nextCursor ?? undefined;
    },
  });
};
