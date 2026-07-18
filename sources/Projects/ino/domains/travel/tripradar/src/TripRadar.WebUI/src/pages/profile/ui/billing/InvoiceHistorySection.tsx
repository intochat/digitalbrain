import { ExternalLink, FileText } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useInfiniteInvoicesQuery } from 'entities/payment/api';
import { Button, SectionEmpty, SectionError } from 'shared/ui';
import { capitalize, formatDate, formatPrice, getStatusColor } from './billingUtils';
import { InvoiceHistorySkeleton } from './InvoiceHistorySkeleton';

const HIDDEN_INVOICE_STATUSES = new Set(['open', 'draft']);

export const InvoiceHistorySection = () => {
  const { t, language } = useFrontendLanguage();
  const { data, isLoading, error, refetch, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useInfiniteInvoicesQuery();

  const allInvoices = data?.pages.flatMap(page => page.invoices ?? []) ?? [];
  const visibleInvoices = allInvoices.filter(
    invoice => !invoice.status || !HIDDEN_INVOICE_STATUSES.has(invoice.status.toLowerCase())
  );

  if (isLoading) {
    return <InvoiceHistorySkeleton />;
  }

  if (error) {
    return <SectionError message={t('Failed to load payment history')} onRetry={() => refetch()} />;
  }

  if (!visibleInvoices.length) {
    return (
      <SectionEmpty
        message={t('No invoices yet')}
        icon={<FileText className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
      />
    );
  }

  return (
    <div>
      <h3 className="text-sm font-medium text-content-secondary dark:text-content-secondary-dark mb-4">
        {t('Payment History')}
      </h3>

      <div className="hidden sm:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-outline dark:border-outline-dark">
              <th className="text-left pb-2 text-xs font-medium text-content-muted dark:text-content-muted-dark">
                {t('Date')}
              </th>
              <th className="text-left pb-2 text-xs font-medium text-content-muted dark:text-content-muted-dark">
                {t('Amount')}
              </th>
              <th className="text-left pb-2 text-xs font-medium text-content-muted dark:text-content-muted-dark">
                {t('Status')}
              </th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {visibleInvoices.map(invoice => {
              const statusColors = invoice.status ? getStatusColor(invoice.status) : null;
              return (
                <tr
                  key={invoice.number ?? invoice.cursor}
                  className="border-b border-outline/50 dark:border-outline-dark/50 last:border-b-0"
                >
                  <td className="py-2.5 text-content dark:text-content-dark">
                    {invoice.createdAt ? formatDate(invoice.createdAt, language) : '—'}
                  </td>
                  <td className="py-2.5 text-content dark:text-content-dark">
                    {invoice.amountDue != null ? formatPrice(invoice.amountDue, invoice.currency, language) : '—'}
                  </td>
                  <td className="py-2.5">
                    {statusColors ? (
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusColors.bg} ${statusColors.text}`}
                      >
                        {t(capitalize(invoice.status!))}
                      </span>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td className="py-2.5 text-right">
                    {invoice.hostedInvoiceUrl ? (
                      <a
                        href={invoice.hostedInvoiceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1 text-xs text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
                      >
                        <ExternalLink className="h-3 w-3" />
                        {t('View')}
                      </a>
                    ) : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="sm:hidden space-y-0">
        {visibleInvoices.map(invoice => {
          const statusColors = invoice.status ? getStatusColor(invoice.status) : null;
          return (
            <div
              key={invoice.number ?? invoice.cursor}
              className="py-2.5 border-b border-outline/50 dark:border-outline-dark/50 last:border-b-0"
            >
              <div className="flex items-center justify-between mb-0.5">
                <span className="text-sm text-content dark:text-content-dark">
                  {invoice.createdAt ? formatDate(invoice.createdAt, language) : '—'}
                </span>
                {statusColors ? (
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusColors.bg} ${statusColors.text}`}
                  >
                    {t(capitalize(invoice.status!))}
                  </span>
                ) : null}
              </div>
              <div className="flex items-center justify-between">
                <span className="text-sm text-content dark:text-content-dark">
                  {invoice.amountDue != null ? formatPrice(invoice.amountDue, invoice.currency, language) : '—'}
                </span>
                {invoice.hostedInvoiceUrl ? (
                  <a
                    href={invoice.hostedInvoiceUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 text-xs text-content-muted dark:text-content-muted-dark hover:text-content dark:hover:text-content-dark transition-colors"
                  >
                    <ExternalLink className="h-3 w-3" />
                    {t('View')}
                  </a>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>

      {hasNextPage && (
        <div className="flex justify-center pt-4">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
            isLoading={isFetchingNextPage}
          >
            {t('Load more')}
          </Button>
        </div>
      )}
    </div>
  );
};
