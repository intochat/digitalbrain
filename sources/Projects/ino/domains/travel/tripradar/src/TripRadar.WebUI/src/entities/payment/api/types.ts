export interface SubscriptionResponse {
  status: string;
  tierType: string;
  billingPeriod: string;
  currentPeriodStart: string;
  currentPeriodEnd: string;
  cancelAtPeriodEnd: boolean;
  canceledAt?: string | null;
  priceAmount: number;
  currency: string;
  nextInvoiceDate?: string | null;
  trialEnd?: string | null;
  discountPercent?: number | null;
  pendingTierType?: string | null;
  pendingTierEffectiveDate?: string | null;
}

export interface InvoiceItemResponse {
  cursor: string;
  number?: string | null;
  status?: string | null;
  currency?: string | null;
  createdAt: string;
  amountDue: number;
  amountPaid: number;
  dueDate?: string | null;
  paidAt?: string | null;
  invoicePdfUrl?: string | null;
  hostedInvoiceUrl?: string | null;
  description?: string | null;
  subscriptionId?: string | null;
  cardBrand?: string | null;
  cardLast4?: string | null;
  paymentMethodType?: string | null;
  receiptUrl?: string | null;
}

export interface InvoicesResponse {
  invoices: InvoiceItemResponse[];
  limit: number;
  startingAfter?: string | null;
  status?: string | null;
  hasMore: boolean;
  nextCursor?: string | null;
}

export interface InvoicesQueryParams {
  limit?: number;
  startingAfter?: string;
  status?: string;
}

export type RefundReasonType =
  | 'requestedByCustomer'
  | 'duplicate'
  | 'fraudulent'
  | 'subscriptionCanceled'
  | 'serviceNotDelivered';

export interface CreateRefundRequest {
  reason: RefundReasonType;
  metadata?: Record<string, string> | null;
}

export interface CreateRefundResponse {
  refundId?: string | null;
  paymentIntentId?: string | null;
  amount?: number;
  currency?: string | null;
  status?: string | null;
  reason?: string | null;
  created?: string;
  metadata?: Record<string, string> | null;
}

export interface OverageUsageResponse {
  username?: string | null;
  tierName?: string | null;
  regularTokensUsed?: number;
  overageTokensUsed?: number;
  totalOverageCharges?: number;
  currency?: string | null;
  year?: number;
  month?: number;
  isEligibleForOverage?: boolean;
  payAsYouGoEnabled?: boolean;
}

export interface UpdateMeteredBillingRequest {
  enabled: boolean;
}

export interface UpdatePayAsYouGoResponse {
  enabled?: boolean;
}

export interface CancelSubscriptionResponse {
  message?: string;
  Message?: string;
}

export interface ToggleSubscriptionRequest {
  activate: boolean;
}

export interface ToggleSubscriptionResponse {
  message: string;
  status: string;
}

export interface PaymentMethodCardResponse {
  brand: string;
  last4: string;
  expMonth: number;
  expYear: number;
  country?: string | null;
}

export interface PaymentMethodBillingAddressResponse {
  country?: string | null;
  postalCode?: string | null;
}

export interface PaymentMethodBillingDetailsResponse {
  name?: string | null;
  email?: string | null;
  address?: PaymentMethodBillingAddressResponse | null;
}

export interface PaymentMethodResponse {
  id: string;
  type: string;
  card: PaymentMethodCardResponse;
  billingDetails?: PaymentMethodBillingDetailsResponse | null;
  isDefault: boolean;
  createdAt: string;
}

export interface PaymentMethodsResponse {
  paymentMethods: PaymentMethodResponse[];
  hasActiveSubscription: boolean;
}

export interface UpdateDefaultPaymentMethodRequest {
  brand?: string | null;
  last4: string;
  expMonth: number;
  expYear: number;
  setAsDefault?: boolean;
}

export interface UpdateDefaultPaymentMethodResponse {
  message: string;
  defaultPaymentMethodLast4?: string | null;
  defaultPaymentMethodExpMonth?: number | null;
  defaultPaymentMethodExpYear?: number | null;
}

export interface DeletePaymentMethodRequest {
  brand?: string | null;
  last4: string;
  expMonth: number;
  expYear: number;
}

export interface DeletePaymentMethodResponse {
  message: string;
  newDefaultPaymentMethodLast4?: string | null;
  newDefaultPaymentMethodExpMonth?: number | null;
  newDefaultPaymentMethodExpYear?: number | null;
  remainingPaymentMethods: number;
}

export interface DowngradeSubscriptionRequest {
  targetTierType: string;
  billingPeriodType: string;
}

export interface DowngradeSubscriptionResponse {
  message?: string;
  Message?: string;
}

export interface CreateSetupIntentResponse {
  clientSecret?: string | null;
}

export interface SubscriptionCheckoutResponse {
  clientSecret: string;
  currency: string;
  amountSubtotal: number;
  amountDiscount: number;
  amountTotal: number;
  promoCode?: string | null;
}
