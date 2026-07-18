import type {
  CancelSubscriptionRequest,
  CreateRefundResponse,
  CreateSetupIntentResponse,
  CreateSubscriptionCheckoutRequest,
  DeletePaymentMethodByCardRequest,
  DeletePaymentMethodResponse,
  DowngradeTierRequest,
  GetInvoicesResponse,
  GetPaymentMethodsResponse,
  GetUsageSummaryResponse,
  GetUserSubscriptionResponse,
  OverageUsageResponse,
  PricesResponse,
  RefundRequest,
  ToggleSubscriptionRequest,
  ToggleSubscriptionResponse,
  UpdateDefaultPaymentMethodRequest,
  UpdateDefaultPaymentMethodResponse,
  UpdateMeteredBillingRequest,
  UpdatePayAsYouGoResponse,
  ValidatePromoCodeRequest,
  ValidatePromoCodeResponse,
} from 'shared/api';
import { apiClient } from 'shared/api';
import type { SubscriptionCheckoutResponse } from './types';

export interface GetInvoicesParams {
  limit?: number;
  startingAfter?: string;
  status?: string;
}

export const paymentApi = {
  createCheckout: async (data: CreateSubscriptionCheckoutRequest): Promise<SubscriptionCheckoutResponse> => {
    const endpoint = '/api/v1.0/payments/subscription-checkouts';
    return apiClient.post(endpoint, data);
  },

  getSubscription: async (): Promise<GetUserSubscriptionResponse> => {
    const endpoint = '/api/v1.0/payments/subscriptions';
    return apiClient.get(endpoint);
  },

  cancelSubscription: async (data: CancelSubscriptionRequest): Promise<void> => {
    const endpoint = '/api/v1.0/payments/subscriptions';
    return apiClient.delete(endpoint, data);
  },

  downgradeSubscription: async (data: DowngradeTierRequest): Promise<void> => {
    const endpoint = '/api/v1.0/payments/subscriptions';
    return apiClient.patch(endpoint, data);
  },

  toggleSubscription: async (data: ToggleSubscriptionRequest): Promise<ToggleSubscriptionResponse> => {
    const endpoint = '/api/v1.0/payments/subscriptions/toggle';
    return apiClient.post(endpoint, data);
  },

  createSetupIntent: async (): Promise<CreateSetupIntentResponse> => {
    const endpoint = '/api/v1.0/payments/setup-intents';
    return apiClient.post(endpoint);
  },

  getPaymentMethods: async (): Promise<GetPaymentMethodsResponse> => {
    const endpoint = '/api/v1.0/payments/payment-methods';
    return apiClient.get(endpoint);
  },

  setDefaultPaymentMethod: async (
    data: UpdateDefaultPaymentMethodRequest
  ): Promise<UpdateDefaultPaymentMethodResponse> => {
    const endpoint = '/api/v1.0/payments/payment-methods';
    return apiClient.patch(endpoint, data);
  },

  deletePaymentMethod: async (data: DeletePaymentMethodByCardRequest): Promise<DeletePaymentMethodResponse> => {
    const endpoint = '/api/v1.0/payments/payment-methods';
    return apiClient.delete(endpoint, data);
  },

  getInvoices: async (params?: GetInvoicesParams): Promise<GetInvoicesResponse> => {
    const searchParams = new URLSearchParams();
    if (params?.limit !== undefined) searchParams.set('limit', params.limit.toString());
    if (params?.startingAfter) searchParams.set('startingAfter', params.startingAfter);
    if (params?.status) searchParams.set('status', params.status);
    const query = searchParams.toString();
    const endpoint = `/api/v1.0/payments/invoices${query ? `?${query}` : ''}`;
    return apiClient.get(endpoint);
  },

  getUsageSummary: async (): Promise<GetUsageSummaryResponse> => {
    const endpoint = '/api/v1.0/payments/usage-summary';
    return apiClient.get(endpoint);
  },

  getOverageUsage: async (): Promise<OverageUsageResponse> => {
    const endpoint = '/api/v1.0/payments/overage-usages';
    return apiClient.get(endpoint);
  },

  togglePayAsYouGo: async (data: UpdateMeteredBillingRequest): Promise<UpdatePayAsYouGoResponse> => {
    const endpoint = '/api/v1.0/payments/metered-events';
    return apiClient.patch(endpoint, data);
  },

  createRefund: async (data: RefundRequest): Promise<CreateRefundResponse> => {
    const endpoint = '/api/v1.0/payments/refunds';
    return apiClient.post(endpoint, data);
  },

  getPrices: async (): Promise<PricesResponse> => {
    const endpoint = '/api/v1.0/payments/prices';
    return apiClient.get(endpoint);
  },

  validatePromoCode: async (data: ValidatePromoCodeRequest): Promise<ValidatePromoCodeResponse> => {
    const endpoint = '/api/v1.0/payments/promo-codes/validate';
    return apiClient.post(endpoint, data);
  },
};
