import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { paymentKeys } from 'entities/payment/api';

export type CheckoutStatus = 'success' | 'cancel';

export const CHECKOUT_PARAM = 'checkout';
export const SESSION_ID_PARAM = 'session_id';
const POST_CHECKOUT_SYNC_INTERVAL_MS = 2000;
const POST_CHECKOUT_SYNC_ATTEMPTS = 8;

type PaymentMethodSummary = {
  id?: string | null;
  isDefault?: boolean | null;
};

type PaymentMethodsQueryData = {
  paymentMethods?: PaymentMethodSummary[];
};

type InvoiceSummary = {
  number?: string | null;
  cursor?: string | null;
  status?: string | null;
  createdAt?: string | null;
};

type InvoicesPage = {
  invoices?: InvoiceSummary[];
};

type InvoicesQueryData = {
  pages?: InvoicesPage[];
};

type SubscriptionQueryData = {
  status?: string | null;
  nextInvoiceDate?: string | null;
  tierType?: string | null;
  billingPeriod?: string | null;
  priceAmount?: number | null;
  currency?: string | null;
  pendingTierType?: string | null;
};

export type BillingSnapshot = {
  subscriptionStatus: string | null;
  nextInvoiceDate: string | null;
  tierType: string | null;
  billingPeriod: string | null;
  priceAmount: number | null;
  currency: string | null;
  pendingTierType: string | null;
  paymentMethodCount: number;
  defaultPaymentMethodId: string | null;
  latestInvoiceKey: string | null;
  latestInvoiceStatus: string | null;
  invoiceCount: number;
};

export const parseCheckoutStatus = (value: string | null): CheckoutStatus | null => {
  if (value === 'success' || value === 'cancel') return value;
  return null;
};

export const cleanCheckoutParams = (params: URLSearchParams): URLSearchParams => {
  const cleaned = new URLSearchParams(params);
  cleaned.delete(CHECKOUT_PARAM);
  cleaned.delete(SESSION_ID_PARAM);
  return cleaned;
};

export const buildPostCheckoutReloadUrl = (pathname: string, params: URLSearchParams): string => {
  const cleanedParams = cleanCheckoutParams(params).toString();
  return cleanedParams ? `${pathname}?${cleanedParams}` : pathname;
};

export const getBillingSnapshot = (
  subscription?: SubscriptionQueryData,
  paymentMethods?: PaymentMethodsQueryData,
  invoices?: InvoicesQueryData
): BillingSnapshot => {
  const methods = paymentMethods?.paymentMethods ?? [];
  const allInvoices = invoices?.pages?.flatMap(page => page.invoices ?? []) ?? [];
  const latestInvoice = allInvoices[0];
  const defaultPaymentMethod = methods.find(method => method.isDefault) ?? methods[0];

  return {
    subscriptionStatus: subscription?.status ?? null,
    nextInvoiceDate: subscription?.nextInvoiceDate ?? null,
    tierType: subscription?.tierType ?? null,
    billingPeriod: subscription?.billingPeriod ?? null,
    priceAmount: subscription?.priceAmount ?? null,
    currency: subscription?.currency ?? null,
    pendingTierType: subscription?.pendingTierType ?? null,
    paymentMethodCount: methods.length,
    defaultPaymentMethodId: defaultPaymentMethod?.id ?? null,
    latestInvoiceKey: latestInvoice?.number ?? latestInvoice?.cursor ?? latestInvoice?.createdAt ?? null,
    latestInvoiceStatus: latestInvoice?.status ?? null,
    invoiceCount: allInvoices.length,
  };
};

export const hasBillingSnapshotChanged = (previous: BillingSnapshot, next: BillingSnapshot): boolean => {
  return (
    previous.subscriptionStatus !== next.subscriptionStatus ||
    previous.nextInvoiceDate !== next.nextInvoiceDate ||
    previous.tierType !== next.tierType ||
    previous.billingPeriod !== next.billingPeriod ||
    previous.priceAmount !== next.priceAmount ||
    previous.currency !== next.currency ||
    previous.pendingTierType !== next.pendingTierType ||
    previous.paymentMethodCount !== next.paymentMethodCount ||
    previous.defaultPaymentMethodId !== next.defaultPaymentMethodId ||
    previous.latestInvoiceKey !== next.latestInvoiceKey ||
    previous.latestInvoiceStatus !== next.latestInvoiceStatus ||
    previous.invoiceCount !== next.invoiceCount
  );
};

export const useCheckoutStatus = (): void => {
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const { showSuccess, showInfo } = useToast();
  const { t } = useFrontendLanguage();
  const processedRef = useRef<string | null>(null);
  const syncAbortRef = useRef<AbortController | null>(null);

  const rawStatus = searchParams.get(CHECKOUT_PARAM);
  const status = parseCheckoutStatus(rawStatus);
  const sessionId = searchParams.get(SESSION_ID_PARAM) ?? '';
  const statusKey = status ? `${status}:${sessionId}` : null;

  useEffect(() => {
    if (!status || !statusKey || processedRef.current === statusKey) {
      return;
    }

    processedRef.current = statusKey;

    if (status === 'success') {
      syncAbortRef.current?.abort();
      const abortController = new AbortController();
      syncAbortRef.current = abortController;

      const baselineSnapshot = getBillingSnapshot(
        queryClient.getQueryData<SubscriptionQueryData>(paymentKeys.subscription()),
        queryClient.getQueryData<PaymentMethodsQueryData>(paymentKeys.paymentMethods()),
        queryClient.getQueryData<InvoicesQueryData>(paymentKeys.invoices())
      );

      showSuccess(t('Payment successful!'), t('Your subscription has been updated.'));
      void queryClient.invalidateQueries({ queryKey: paymentKeys.subscription() });
      void queryClient.invalidateQueries({ queryKey: paymentKeys.paymentMethods() });
      void queryClient.invalidateQueries({ queryKey: paymentKeys.invoices() });
      void queryClient.invalidateQueries({ queryKey: ['profile'] });

      const reloadPage = () => {
        const reloadUrl = buildPostCheckoutReloadUrl(window.location.pathname, searchParams);
        window.history.replaceState(window.history.state, '', reloadUrl);
        window.location.reload();
      };

      const syncBillingState = async () => {
        for (let attempt = 0; attempt < POST_CHECKOUT_SYNC_ATTEMPTS; attempt += 1) {
          if (abortController.signal.aborted) {
            return;
          }

          await Promise.all([
            queryClient.refetchQueries({ queryKey: paymentKeys.subscription(), type: 'active' }),
            queryClient.refetchQueries({ queryKey: paymentKeys.paymentMethods(), type: 'active' }),
            queryClient.refetchQueries({ queryKey: paymentKeys.invoices(), type: 'active' }),
            queryClient.refetchQueries({ queryKey: ['profile'], type: 'active' }),
          ]);

          if (abortController.signal.aborted) {
            return;
          }

          const currentSnapshot = getBillingSnapshot(
            queryClient.getQueryData<SubscriptionQueryData>(paymentKeys.subscription()),
            queryClient.getQueryData<PaymentMethodsQueryData>(paymentKeys.paymentMethods()),
            queryClient.getQueryData<InvoicesQueryData>(paymentKeys.invoices())
          );

          if (hasBillingSnapshotChanged(baselineSnapshot, currentSnapshot)) {
            reloadPage();
            return;
          }

          await new Promise(resolve => window.setTimeout(resolve, POST_CHECKOUT_SYNC_INTERVAL_MS));
        }

        if (abortController.signal.aborted) {
          return;
        }

        reloadPage();
      };

      void syncBillingState();
      return;
    }

    showInfo(t('Payment cancelled'), t('Your subscription was not changed.'));
    setSearchParams(cleanCheckoutParams(searchParams), { replace: true });
  }, [queryClient, searchParams, setSearchParams, showInfo, showSuccess, status, statusKey, t]);

  useEffect(() => {
    return () => {
      syncAbortRef.current?.abort();
    };
  }, []);
};
