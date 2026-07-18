// Import and re-export generated types
import type { components } from './generated-types';
export type { components } from './generated-types';
export type {
  CreateTelegramLoginRequest,
  LinkTelegramRequest,
  LinkTelegramResponse,
  LoginErrorTelegramRequired,
  TelegramAuthApiData,
  TelegramData,
  TelegramUsernameSyncRequest,
} from './types';

// Commonly used types
export type UserTierType = components['schemas']['UserTierType'];
export type BillingPeriodType = components['schemas']['BillingPeriodType'];
export type CreateUserRequest = components['schemas']['CreateUserRequest'];
export type CreateLoginRequest = components['schemas']['CreateLoginRequest'];
export type CreateGoogleLoginRequest = components['schemas']['CreateGoogleLoginRequest'];
export type CreateRefreshTokenRequest = components['schemas']['CreateRefreshTokenRequest'];
export type ForgotPasswordRequest = components['schemas']['ForgotPasswordRequest'];
export type ResetPasswordRequest = components['schemas']['ResetPasswordRequest'];
export type UserManagementResponse = components['schemas']['UserManagementResponse'];
export type CreateSubscriptionCheckoutRequest = components['schemas']['CreateSubscriptionCheckoutRequest'];
export type CreateSubscriptionCheckoutResponse = components['schemas']['CreateSubscriptionCheckoutResponse'];
export type GetUserProfileResponse = components['schemas']['GetUserProfileResponse'];
export type UpdateUserProfileRequest = components['schemas']['UpdateUserProfileRequest'];
export type UpdateUserProfileResponse = components['schemas']['UpdateUserProfileResponse'];
export type GetUserTierUsageResponse = components['schemas']['GetUserTierUsageResponse'];
export type PricesResponse = components['schemas']['PricesResponse'];
export type PriceResponse = components['schemas']['PriceResponse'];
export type GetLoginResponse = components['schemas']['GetLoginResponse'];

// Payment types
export type GetUserSubscriptionResponse = components['schemas']['GetUserSubscriptionResponse'];
export type CancelSubscriptionRequest = components['schemas']['CancelSubscriptionRequest'];
export type DowngradeTierRequest = components['schemas']['DowngradeTierRequest'];
export type ToggleSubscriptionRequest = components['schemas']['ToggleSubscriptionRequest'];
export type ToggleSubscriptionResponse = components['schemas']['ToggleSubscriptionResponse'];
export type CreateSetupIntentResponse = components['schemas']['CreateSetupIntentResponse'];
export type GetPaymentMethodsResponse = components['schemas']['GetPaymentMethodsResponse'];
export type UpdateDefaultPaymentMethodRequest = components['schemas']['UpdateDefaultPaymentMethodRequest'];
export type UpdateDefaultPaymentMethodResponse = components['schemas']['UpdateDefaultPaymentMethodResponse'];
export type DeletePaymentMethodByCardRequest = components['schemas']['DeletePaymentMethodByCardRequest'];
export type DeletePaymentMethodResponse = components['schemas']['DeletePaymentMethodResponse'];
export type GetInvoicesResponse = components['schemas']['GetInvoicesResponse'];
export type GetUsageSummaryResponse = components['schemas']['GetUsageSummaryResponse'];
export type OverageUsageResponse = components['schemas']['OverageUsageResponse'];
export type UpdateMeteredBillingRequest = components['schemas']['UpdateMeteredBillingRequest'];
export type UpdatePayAsYouGoResponse = components['schemas']['UpdatePayAsYouGoResponse'];
export type RefundRequest = components['schemas']['RefundRequest'];
export type CreateRefundResponse = components['schemas']['CreateRefundResponse'];
export type ValidatePromoCodeRequest = components['schemas']['ValidatePromoCodeRequest'];
export type ValidatePromoCodeResponse = components['schemas']['ValidatePromoCodeResponse'];

// Preferences types
export type GetUserPreferencesResponse = components['schemas']['GetUserPreferencesResponse'];
export type UpdateUserPreferencesRequest = components['schemas']['UpdateUserPreferencesRequest'];
export type UserPreferences = components['schemas']['UserPreferences'];
export type UserPreference = components['schemas']['UserPreference'];

// Individual preference types
export type FlightPreferences = components['schemas']['FlightPreferences'];
export type HotelPreferences = components['schemas']['HotelPreferences'];
export type EventPreferences = components['schemas']['EventPreferences'];
export type LocalPlacesPreferences = components['schemas']['LocalPlacesPreferences'];
export type MapsPreferences = components['schemas']['MapsPreferences'];
export type PlaceReviewPreferences = components['schemas']['PlaceReviewPreferences'];

// Enum types
export type TravelClassType = components['schemas']['TravelClassType'];
export type HotelSortByType = components['schemas']['HotelSortByType'];
export type HotelRatingFilterType = components['schemas']['HotelRatingFilterType'];
export type ServiceType = components['schemas']['ServiceType'];

// Export existing API client
export * from './interceptors';
