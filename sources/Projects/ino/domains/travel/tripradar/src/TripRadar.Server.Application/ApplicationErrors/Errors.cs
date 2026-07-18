using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.ApplicationErrors;

public static class Errors
{
    #region Domain

    public static readonly Error FlightQueryDataNotFound = CreateNotFoundError("FLIGHT_QUERY_DATA_NOT_FOUND", "flight query data");

    public static readonly Error FlightExploreDataNotFound = CreateNotFoundError("FLIGHT_EXPLORE_DATA_NOT_FOUND", "flight explore data");

    public static readonly Error HotelQueryDataNotFound = CreateNotFoundError("HOTEL_QUERY_DATA_NOT_FOUND", "hotel query data");

    public static readonly Error EventQueryDataNotFound = CreateNotFoundError("EVENT_QUERY_DATA_NOT_FOUND", "event query data");

    public static readonly Error LocalPlacesQueryDataNotFound = CreateNotFoundError("LOCAL_PLACES_QUERY_DATA_NOT_FOUND", "local places query data");

    public static readonly Error MapsQueryDataNotFound = CreateNotFoundError("MAPS_QUERY_DATA_NOT_FOUND", "maps query data");

    public static readonly Error PlaceReviewsQueryDataNotFound = CreateNotFoundError("PLACE_REVIEWS_QUERY_DATA_NOT_FOUND", "place reviews query data");

    public static readonly Error MapsDirectionsDataNotFound = CreateNotFoundError("MAPS_DIRECTIONS_DATA_NOT_FOUND", "maps directions data");

    public static readonly Error MapsPlaceResultsDataNotFound = CreateNotFoundError("MAPS_PLACE_RESULTS_DATA_NOT_FOUND", "maps place results data");

    public static readonly Error TripAdvisorSearchDataNotFound = CreateNotFoundError("TRIPADVISOR_SEARCH_DATA_NOT_FOUND", "TripAdvisor search data");

    public static readonly Error TripAdvisorPlaceDataNotFound = CreateNotFoundError("TRIPADVISOR_PLACE_DATA_NOT_FOUND", "TripAdvisor place data");

    public static readonly Error TripAdvisorDomainNotFound = CreateNotFoundError("TRIPADVISOR_DOMAIN", "TripAdvisor domain");

    public static readonly Error OpenTableReviewDataNotFound = CreateNotFoundError("OPENTABLE_REVIEW_DATA_NOT_FOUND", "OpenTable reviews data");

    public static readonly Error OpenTableDomainNotFound = CreateNotFoundError("OPENTABLE_DOMAIN", "OpenTable domain");

    public static readonly Error YelpSearchDataNotFound = CreateNotFoundError("YELP_SEARCH_DATA_NOT_FOUND", "Yelp search data");

    public static readonly Error YelpPlaceDataNotFound = CreateNotFoundError("YELP_PLACE_DATA_NOT_FOUND", "Yelp place data");

    public static readonly Error YelpReviewsDataNotFound = CreateNotFoundError("YELP_REVIEWS_DATA_NOT_FOUND", "Yelp reviews data");

    public static readonly Error YelpPlaceFullMenuDataNotFound = CreateNotFoundError("YELP_PLACE_FULL_MENU_DATA_NOT_FOUND", "Yelp place full menu data");

    public static readonly Error YelpDomainNotFound = CreateNotFoundError("YELP_DOMAIN", "Yelp domain");

    public static readonly Error YelpReviewLanguageNotFound = CreateNotFoundError("YELP_REVIEW_LANGUAGE", "Yelp review language");

    public static readonly Error YouTubeSearchDataNotFound = CreateNotFoundError("YOUTUBE_SEARCH_DATA_NOT_FOUND", "YouTube search data");

    public static readonly Error GoogleLightSearchDataNotFound = CreateNotFoundError("GOOGLE_LIGHT_SEARCH_DATA_NOT_FOUND", "Google Light search data");

    public static readonly Error GoogleLrLanguageNotFound = CreateNotFoundError("GOOGLE_LR_LANGUAGE", "Google LR language");

    public static readonly Error UserNotFound = CreateNotFoundError("USER", "user");

    public static readonly Error UserExists = CreateExistsError("USER", "user");

    public static readonly Error UserIpNotValidOrNotProvided = new("USER_IP_NOT_VALID_OR_NOT_PROVIDED", "Either not valid user id address or IP address provided.");

    public static readonly Error UserDisabled = new("USER_DISABLED", "User is disabled.");

    public static readonly Error UserConsentNotGranted = new("USER_CONSENT_NOT_GRANTED", "User must provide data storage consent to create an account.");

    public static readonly Error EmailAlreadyConfirmed = new("EMAIL_ALREADY_CONFIRMED", "Email address is already confirmed.");

    public static readonly Error EmailNotFound = CreateNotFoundError("EMAIL", "email");

    public static readonly Error InvalidEmailConfirmationToken = new("INVALID_EMAIL_CONFIRMATION_TOKEN", "Invalid or expired email confirmation token.");

    public static readonly Error InvalidPasswordResetToken = new("INVALID_PASSWORD_RESET_TOKEN", "Invalid or expired password reset token.");

    public static readonly Error CurrentPasswordIncorrect = new("CURRENT_PASSWORD_INCORRECT", "The current password is incorrect.");

    public static readonly Error EmailNotConfirmed = new("EMAIL_NOT_CONFIRMED", "Please confirm your email before logging in");

    public static readonly Error TelegramRequired = new("TELEGRAM_REQUIRED", "This account must be linked to Telegram before sign-in can continue.");

    public static readonly Error TelegramAuthInvalid = new("TELEGRAM_AUTH_INVALID", "Invalid Telegram authentication. The provided hash does not match the expected value.");

    public static readonly Error TelegramAccountNotLinked = new("TELEGRAM_ACCOUNT_NOT_LINKED", "No TripRadar account is linked to this Telegram profile. Sign in with email or Google first, then connect Telegram.");

    public static readonly Error AccountLocked = new("ACCOUNT_LOCKED", "Account is locked.");

    public static readonly Error PaymentNotFound = CreateNotFoundError("PAYMENT", "payment");

    public static readonly Error PaymentInitiationFailed = new("PAYMENT_INITIATION_FAILED", "Failed to initiate payment process.");

    public static readonly Error PaymentProcessingFailed = new("PAYMENT_PROCESSING_FAILED", "Failed to process payment.");

    public static readonly Error StripeAuthenticationFailed = new("STRIPE_AUTHENTICATION_FAILED", "Stripe API authentication failed. Please check API keys configuration.");

    public static readonly Error StripeApiConnectionFailed = new("STRIPE_API_CONNECTION_FAILED", "Failed to connect to Stripe API. Please check network connectivity.");

    public static readonly Error StripeInvalidRequestFailed = new("STRIPE_INVALID_REQUEST", "Invalid request to Stripe API. Please check the payment parameters.");

    public static readonly Error StripeCheckoutSessionCreationFailed = new("STRIPE_CHECKOUT_SESSION_CREATION_FAILED", "Failed to create Stripe checkout session.");

    public static readonly Error SameTierUpgrade = new("SAME_TIER_UPGRADE", "User is already on the target tier.");

    public static readonly Error TierPriceNotFound = new("TIER_PRICE_NOT_FOUND", "Price for the specified tier was not found.");

    public static readonly Error RefundNotAllowed = new("REFUND_NOT_ALLOWED", "Refund cannot be processed. You have exceeded the Basic tier limit (5 tokens per day) during your subscription period. After refund, you would be downgraded to Basic tier, but you have already consumed more tokens than this tier allows.");

    public static readonly Error PayAsYouGoNotEnabled = new("OVERAGE_NOT_ENABLED", "Overage billing is not enabled for this user or tier.");

    public static readonly Error InsufficientTokens = new("INSUFFICIENT_TOKENS", "Insufficient tokens available for this operation.");

    public static readonly Error InvalidTokenAmount = new("INVALID_TOKEN_AMOUNT", "Token amount must be a positive value.");

    public static readonly Error PayAsYouGoBillingFailed = new("OVERAGE_BILLING_FAILED", "Failed to process overage billing.");

    public static readonly Error InsufficientSubscriptionTier = new("INSUFFICIENT_SUBSCRIPTION_TIER", "Scheduled queries and query history are only available for active Essential and Advanced tier subscribers. Please upgrade your subscription to access these features.");

    public static readonly Error SubscriptionNotFound = CreateNotFoundError("SUBSCRIPTION", "subscription");

    public static readonly Error PaymentMethodNotFound = CreateNotFoundError("PAYMENT_METHOD", "payment method");

    public static readonly Error PaymentMethodAmbiguous = new("PAYMENT_METHOD_AMBIGUOUS", "Multiple payment methods matched the provided card details. Please provide more details.");

    public static readonly Error CannotRemoveLastPaymentMethod = new("CANNOT_REMOVE_LAST_PAYMENT_METHOD", "Cannot remove the last payment method with an active subscription.");

    public static readonly Error PaymentMethodInUse = new("PAYMENT_METHOD_IN_USE", "Payment method is currently being used in an active payment and cannot be removed.");

    public static readonly Error HasUnpaidInvoices = new("HAS_UNPAID_INVOICES", "Cannot remove the last payment method with unpaid invoices.");

    public static readonly Error AirportCodeNotFound = CreateNotFoundError("AIRPORT_CODE_NOT_FOUND", "airport code");

    public static readonly Error CountryCodeNotFound = CreateNotFoundError("COUNTRY_CODE_NOT_FOUND", "country code");

    public static readonly Error LanguageCodeNotFound = CreateNotFoundError("LANGUAGE_CODE_NOT_FOUND", "language code");

    public static readonly Error TimezoneNotFound = CreateNotFoundError("TIMEZONE", "timezone");

    public static readonly Error AirlineCodeNotFound = CreateNotFoundError("AIRLINE_CODE", "airline code");

    public static readonly Error CurrencyCodeNotFound = CreateNotFoundError("CURRENCY_CODE_NOT_FOUND", "currency code");

    public static readonly Error ScheduledExecutionNotFound = CreateNotFoundError("SCHEDULED_EXECUTION", "scheduled execution");

    public static readonly Error InvalidFlightDates = new("INVALID_FLIGHT_DATES", "Return date must be after departure date.");

    public static readonly Error InvalidFlightRoute = new("INVALID_FLIGHT_ROUTE", "Departure and destination airports must be different.");

    public static readonly Error InvalidHotelDates = new("INVALID_HOTEL_DATES", "Check-out date must be after check-in date.");

    public static readonly Error InvalidScheduledExecutionWindow = new("INVALID_SCHEDULED_EXECUTION_WINDOW", "Next execution time must be on or before the request start date.");

    public static readonly Error LocationNotFound = CreateNotFoundError("LOCATION", "location");

    public static readonly Error SerpApiDeserializationFailed = new("SERPAPI_DESERIALIZATION_FAILED", "Failed to deserialize SerpApi response:");

    public static readonly Error SerpApiRequestFailed = new("SERPAPI_REQUEST_FAILED", "SerpApi request failed (check place ID, query parameters, or service availability):");

    public static readonly Error KiwiCalendarRequestFailed = new("KIWI_CALENDAR_REQUEST_FAILED", "Kiwi calendar request failed (check route, query parameters, API key, or service availability).");

    public static readonly Error InvalidFeedbackCategory = new("INVALID_FEEDBACK_CATEGORY", "Invalid feedback category type.");
   
    public static readonly Error InvalidServiceType = new("INVALID_SERVICE_TYPE", "Invalid service type.");

    public static readonly Error InvalidUsageEventSource = new("INVALID_USAGE_EVENT_SOURCE", "Invalid usage event source.");

    public static readonly Error AiFeatureRequiresPaidTier = new("AI_FEATURE_REQUIRES_PAID_TIER", "AI usage is available only for paid users.");

    public static readonly Error FeedbackRateLimitExceeded = new("FEEDBACK_RATE_LIMIT_EXCEEDED", "Rate limit exceeded.");

    public static readonly Error PromoCodeNotFound = CreateNotFoundError("PROMO_CODE", "promo code");

    public static readonly Error PromoCodeExpired = new("PROMO_CODE_EXPIRED", "The promo code has expired.");

    public static readonly Error PromoCodeInactive = new("PROMO_CODE_INACTIVE", "The promo code is not active.");

    public static readonly Error PromoCodeUsageLimitExceeded = new("PROMO_CODE_USAGE_LIMIT_EXCEEDED", "The promo code usage limit has been reached.");

    public static readonly Error PromoCodeAlreadyUsedByUser = new("PROMO_CODE_ALREADY_USED_BY_USER", "User has already used this promo code.");

    public static readonly Error PromoCodeNotStarted = new("PROMO_CODE_NOT_STARTED", "The promo code is not valid yet.");

    public static readonly Error PromoCodeAlreadyExists = new("PROMO_CODE_ALREADY_EXISTS", "A promo code with this code already exists.");

    public static readonly Error InvalidDiscountType = new("INVALID_DISCOUNT_TYPE", "Invalid discount type.");

    public static readonly Error InvalidPercentage = new("INVALID_PERCENTAGE", "Percentage discount must be between 0 and 100.");

    public static readonly Error InvalidFixedAmount = new("INVALID_FIXED_AMOUNT", "Fixed amount discount must be greater than 0.");

    public static readonly Error TripVaultNotFound = CreateNotFoundError("TRIP_VAULT", "trip vault");

    public static readonly Error TripVaultItemNotFound = CreateNotFoundError("TRIP_VAULT_ITEM", "trip vault item");

    public static readonly Error TripVaultUnauthorizedAccess = new("TRIP_VAULT_UNAUTHORIZED_ACCESS", "User is not authorized to access this trip vault.");

    public static readonly Error TripVaultNameAlreadyExists = CreateExistsError("TRIP_VAULT_NAME", "trip vault name");

    public static readonly Error SearchTypeNotFound = new("SEARCH_TYPE_NOT_FOUND", "Search type not found.");

    public static class PreferenceApplication
    {
        public static readonly Error UnexpectedError = new("PREFERENCE_APPLICATION_UNEXPECTED_ERROR", "An unexpected error occurred while applying preferences");
    }

    #endregion

    #region Technical details

    public static readonly Error UsernameRequired = new("USERNAME_REQUIRED", "Username or email cannot be empty");

    public static readonly Error PasswordRequired = new("PASSWORD_REQUIRED", "Password cannot be empty");

    public static readonly Error PasswordNotValid = new("PASSWORD_NOT_VALID", "Password is invalid");

    public static readonly Error UsernameOrPasswordNotValid = new("USERNAME_OR_PASSWORD_NOT_VALID", "Username or password is invalid");

    public static readonly Error InvalidToken = new("INVALID_TOKEN", "Invalid Clients Token.");

    public static readonly Error RefreshTokenExpired = new("REFRESH_TOKEN_EXPIRED", "Refresh token has expired");

    public static readonly Error RefreshTokenInvalidFormat = new("REFRESH_TOKEN_INVALID_FORMAT", "Invalid token format");

    public static readonly Error RefreshTokenNotFound = new("REFRESH_TOKEN_NOT_FOUND", "Refresh token not found");

    public static readonly Error RefreshTokenRevoked = new("REFRESH_TOKEN_REVOKED", "Refresh token has been revoked");

    public static readonly Error UnauthorizedAccess = new("UNAUTHORIZED_ACCESS", "You are not authorized to access this resource.");

    public static readonly Error SerializationFailed = new("SERIALIZATION_FAILED", "The data cannot be serialized into this data type. See more information: ");

    public static readonly Error DeserializationFailed = new("DESERIALIZATION_FAILED", "The data cannot be deserialized into this data type. See more information: ");

    public static readonly Error CacheDeletionFailed = new("CACHE_DELETION_FAILED", "Failed to delete cache entry. See more information: ");

    public static readonly Error CryptographerNotFound = CreateNotFoundError("CRYPTOGRAPHER_NOT_FOUND", "cryptographer");

    public static readonly Error InternalServerError = new("INTERNAL_ERROR", "An internal error occurred while processing the request.");

    public static readonly Error HttpsRequired = new("HTTPS_REQUIRED", "HTTPS required for authentication");

    public static readonly Error RateLimitExceeded = new("RATE_LIMIT_EXCEEDED", "Rate limit exceeded. Please try again later.");

    public static readonly Error ServiceUnavailable = new("SERVICE_UNAVAILABLE", "The service is temporarily unavailable. Please try again later.");

    public static readonly Error InvalidApiVersion = new("INVALID_API_VERSION", "Invalid API version specified.");

    #endregion

    #region Stripe Webhooks

    public static readonly Error StripeWebhookInvalidSubscription = new("STRIPE_WEBHOOK_101", "Invalid or missing subscription data in webhook event");

    public static readonly Error StripeWebhookInvalidInvoice = new("STRIPE_WEBHOOK_102", "Invalid or missing invoice data in webhook event");

    public static readonly Error StripeWebhookEventProcessingFailed = new("STRIPE_WEBHOOK_201", "Failed to process webhook event");

    public static readonly Error StripeWebhookDatabaseOperationFailed = new("STRIPE_WEBHOOK_205", "Database operation failed during webhook processing");

    public static Error CreateStripeWebhookEventProcessingError(string eventType, string eventId, string details) => new("STRIPE_WEBHOOK_201", $"Failed to process {eventType} event (ID: {eventId}): {details}");

    #endregion

    #region Methods

    private static Error CreateNotFoundError(string code, string entity) => new($"{code}_NOT_FOUND", $"The {entity} was not found.");

    private static Error CreateExistsError(string code, string entity) => new($"{code}_EXISTS", $"The {entity} already exists.");

    public static Error CreateProviderDisabledError(string providerName) => new($"{providerName.ToUpperInvariant()}_PROVIDER_DISABLED", $"The {providerName} provider is currently disabled.");

    #endregion
}
