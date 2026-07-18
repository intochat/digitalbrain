using System.Collections.Immutable;

namespace TripRadar.Server.Application.Constants;

public static class MetricConstants
{
    public const string ApplicationName = "TripRadar";
    public const string GetFlightsRequest = "get-flights-request";
    public const string GetFlightExploreRequest = "get-flight-explore-request";
    public const string GetFlightPriceCalendarRequest = "get-flight-price-calendar-request";
    public const string GetHotelsRequest = "get-hotels-request";
    public const string GetEventsRequest = "get-events-request";
    public const string GetLocalPlacesRequest = "get-local-places-request";
    public const string GetMapsRequest = "get-maps-request";
    public const string GetPlaceReviewsRequest = "get-place-reviews-request";
    public const string GetTripAdvisorSearchRequest = "get-tripadvisor-search-request";
    public const string GetTripAdvisorPlaceRequest = "get-tripadvisor-place-request";
    public const string GetOpenTableReviewsRequest = "get-opentable-reviews-request";
    public const string GetYouTubeSearchRequest = "get-youtube-search-request";
    public const string GetYelpSearchRequest = "get-yelp-search-request";
    public const string GetYelpPlaceRequest = "get-yelp-place-request";
    public const string GetYelpPlaceFullMenuRequest = "get-yelp-place-full-menu-request";
    public const string GetYelpReviewsRequest = "get-yelp-reviews-request";
    public const string GetMapsDirectionsRequest = "get-maps-directions-request";
    public const string GetMapsPlaceResultsRequest = "get-maps-place-results-request";
    public const string GetGoogleLightSearchRequest = "get-google-light-search-request";
    public const string CreateScheduledFlight = "create-scheduled-flight";
    public const string CreateScheduledHotel = "create-scheduled-hotel";
    public const string CreateScheduledEvent = "create-scheduled-event";
    public const string CreateScheduledLocalPlaces = "create-scheduled-local-places";
    public const string GetScheduledExecutionsRequest = "get-scheduled-executions-request";
    public const string DeleteScheduledExecution = "delete-scheduled-execution";
    public const string UpdateScheduledExecutionQuery = "update-scheduled-execution-query";
    public const string UpdateScheduledFlightStatus = "update-scheduled-flight-status";
    public const string CreateFeedback = "create-feedback";
    public const string GetUserFeedback = "get-user-feedback";
    public const string CreateNewUser = "create-new-user";
    public const string ConfirmEmail = "confirm-email";
    public const string ToggleUserStatus = "toggle-user-status";
    public const string ActivateUser = "activate-user";
    public const string UpdateUsername = "update-username";
    public const string DeleteUser = "delete-user";
    public const string ResetPassword = "reset-password";
    public const string ChangePassword = "change-password";
    public const string ResendEmailConfirmation = "resend-email-confirmation";
    public const string GetUserTierUsage = "get-user-tier-usage";
    public const string GetAllPricesRequest = "get_all_prices_request";
    public const string CreateSubscriptionCheckout = "create-subscription-checkout";
    public const string CancelSubscription = "cancel-subscription";
    public const string DowngradeSubscription = "downgrade-subscription";
    public const string CreateSetupIntent = "create-setup-intent";
    public const string CreateRefund = "create-refund";
    public const string GetOverageUsage = "get-overage-usage";
    public const string GetOverageUsers = "get-overage-users";
    public const string UpdatePayAsYouGo = "update-pay-as-you-go";
    public const string UpdateUserPreferences = "update-user-preferences";
    public const string UpdateUserProfile = "update-user-profile";
    public const string Logout = "logout";

    private static readonly ImmutableDictionary<string, string> MetricDescriptions = new Dictionary<string, string>
    {
        { GetFlightsRequest, "Number of flight data retrieval requests" },
        { GetFlightExploreRequest, "Number of flight explore destination requests" },
        { GetFlightPriceCalendarRequest, "Number of flight price calendar requests" },
        { GetHotelsRequest, "Number of hotel data retrieval requests" },
        { GetEventsRequest, "Number of event data retrieval requests" },
        { GetLocalPlacesRequest, "Number of local place data retrieval requests" },
        { GetMapsRequest, "Number of maps data retrieval requests" },
        { GetPlaceReviewsRequest, "Number of place reviews data retrieval requests" },
        { GetTripAdvisorSearchRequest, "Number of TripAdvisor search requests" },
        { GetTripAdvisorPlaceRequest, "Number of TripAdvisor place detail requests" },
        { GetOpenTableReviewsRequest, "Number of OpenTable reviews requests" },
        { GetYouTubeSearchRequest, "Number of YouTube search requests" },
        { GetYelpSearchRequest, "Number of Yelp search requests" },
        { GetYelpPlaceRequest, "Number of Yelp place detail requests" },
        { GetYelpPlaceFullMenuRequest, "Number of Yelp full menu requests" },
        { GetYelpReviewsRequest, "Number of Yelp reviews requests" },
        { GetMapsDirectionsRequest, "Number of maps directions requests" },
        { GetMapsPlaceResultsRequest, "Number of maps place results requests" },
        { GetGoogleLightSearchRequest, "Number of Google Light search requests" },
        { CreateScheduledFlight, "Number of scheduled flight data creation requests" },
        { CreateScheduledHotel, "Number of scheduled hotel data creation requests" },
        { CreateScheduledEvent, "Number of scheduled event data creation requests" },
        { CreateScheduledLocalPlaces, "Number of scheduled local places data creation requests" },
        { GetScheduledExecutionsRequest, "Number of scheduled execution list retrieval requests" },
        { DeleteScheduledExecution, "Number of scheduled execution deletion requests" },
        { UpdateScheduledExecutionQuery, "Number of scheduled execution query update requests" },
        { UpdateScheduledFlightStatus, "Number of scheduled flight status updates" },
        { CreateFeedback, "Number of feedback creation requests" },
        { GetUserFeedback, "Number of user feedback retrieval requests" },
        { CreateNewUser, "Number of new user registration requests" },
        { ConfirmEmail, "Number of email confirmation requests" },
        { ToggleUserStatus, "Number of user status toggle requests" },
        { ActivateUser, "Number of user activation requests" },
        { UpdateUsername, "Number of username update requests" },
        { DeleteUser, "Number of user deletion requests" },
        { ResetPassword, "Number of password reset requests" },
        { ChangePassword, "Number of password change requests" },
        { ResendEmailConfirmation, "Number of email confirmation resend requests" },
        { GetUserTierUsage, "Number of user tier usage retrieval requests" },
        { CreateSubscriptionCheckout, "Number of subscription checkout creation requests" },
        { CancelSubscription, "Number of subscription cancellation requests" },
        { DowngradeSubscription, "Number of subscription downgrade requests" },
        { CreateSetupIntent, "Number of setup intent creation requests" },
        { CreateRefund, "Number of refund creation requests" },
        { GetOverageUsage, "Number of overage usage retrieval requests" },
        { GetOverageUsers, "Number of overage users list retrieval requests" },
        { UpdatePayAsYouGo, "Number of PAYG opt-in/out updates" },
        { UpdateUserPreferences, "Number of user preferences update requests" },
        { UpdateUserProfile, "Number of user profile update requests" },
        { Logout, "Number of logout requests" }
    }.ToImmutableDictionary();

    public static string GetDescription(string metricName) =>
        MetricDescriptions.TryGetValue(metricName, out var description)
            ? description
            : $"Counter for {metricName}";
}
